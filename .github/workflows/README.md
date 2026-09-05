# CI/CD Pipeline — PNGCrushCS

> Everything in this folder is the automated pipeline for this repository.
> Workflows live here, their helper scripts live in `scripts/`.

## What this does

Seven workflows, one shared build block, three helper scripts:

| File                            | Trigger                                  | Purpose                                          |
|---------------------------------|------------------------------------------|--------------------------------------------------|
| `ci.yml`                        | pull request + `workflow_call`           | The merge gate: build + tests on ubuntu + windows |
| `smoke.yml`                     | push to branches other than `main`       | Fast tier on one runner: "did I just break it?"  |
| `viewer-platforms.yml`          | push to non-`main`, viewer paths only    | Viewer builds on ubuntu + windows + macos        |
| `viewer-screenshot.yml`         | push to branches other than `main`       | Build viewer + refresh README screenshot         |
| `coverage.yml`                  | daily cron 03:41 UTC + manual            | Coverage report, gating nobody                   |
| `nightly.yml`                   | push to `main`                           | Publish `nightly-YYYYMMDD` prerelease            |
| `release.yml`                   | manual dispatch only                     | Cut a release, push the NuGet packages, tag it   |
| `_build.yml`                    | `workflow_call` (internal)               | Shared CLI + Viewer publish/zip block            |
| `scripts/version.pl`            | invoked by the workflows                 | Compute `X.Y.Z.BUILD` + stamp csprojs            |
| `scripts/update-changelog.mjs`  | invoked by the workflows                 | Bucketise commits into release notes             |
| `scripts/prune-nightlies.mjs`   | invoked by the workflows                 | 3-gen (GFS) retention of nightlies               |

## How it works

```text
    push to a working branch            pull request
             │                                │
    ┌────────┼────────────┐                   ▼
    ▼        ▼            ▼           ┌───────────────┐
 smoke   viewer-      viewer-         │    ci.yml     │──► solution build + filtered
 .yml    platforms    screenshot      └───────┬───────┘    tests on ubuntu + windows
    │      .yml         .yml                  │
    │        │            │                   │ merge
    │        │            └──► commit changed │
    │        │                 docs/screen-   ▼
    │        │                 shots/*.png   push to main
    │        │                                │
    └────────┴────────────┐                   ▼
                          │            ┌─────────────┐
   manual dispatch        │            │  nightly    │
          │               │            │   .yml      │
          ▼               │            └─────┬───────┘
   ┌──────────┐           │                  │
   │ release  │           │                  ▼
   │  .yml    │           │       nightly-YYYYMMDD (prerelease)
   └────┬─────┘           │                  │
        │                 │                  ▼
        ▼                 │        scripts/prune-nightlies.mjs
  ci.yml (workflow_call)  │        (GFS: 7 daily + 4 weekly + 3 monthly)
        │                 │
        ▼                 │        ┌──────────────┐
  GH Release v1.2.3       └───────►│ coverage.yml │◄── daily cron
  + three NuGet packages           └──────────────┘
        │
        └──► both release and nightly call _build.yml
```

## Test tiers

Nineteen test projects under `Tests/`. Every leg filters by `TestCategory` — never `Category`,
which filters the reporting and still executes the excluded fixtures:

| Category                                   | ci.yml (the gate) | smoke.yml | Purpose                        |
|--------------------------------------------|-------------------|-----------|--------------------------------|
| `Unit`, `Integration`, `Conformance`, `EndToEnd`, `Exhaustive`, `EdgeCase`, `CwSibling` | ✓ required | ✓ | Everything that decides correctness |
| `Regression`                               | excluded          | excluded  | Heavy regression suite         |
| `Performance`                              | advisory only     | excluded  | Microbenchmarks                |

`Performance` still runs on the gate, as a separate advisory leg: wall-clock timings must not redden
a pull request on a slow shared runner. `smoke.yml` also excludes `Slow`, which is the shared
template's own tier name; nothing in this repository carries it today, and the exclusion is there so
that adding one does not silently lengthen the fast tier.

## What it's for

- Every pull request is built and tested on ubuntu + windows before it can merge.
- Every push to a working branch gets the fast tier back in minutes, on one runner.
- Every non-`main` branch push refreshes the checked-in viewer screenshot when the rendered UI changes.
- Every merge to `main` produces a **tested** nightly prerelease.
- A stable release is cut by hand, and pushes `Hawkynt.FileFormats.Images`,
  `Hawkynt.FileFormats.Video` and `Hawkynt.ImageTransformUI` to nuget.org before it tags anything.
- Old nightlies are auto-pruned on a **Grandfather-Father-Son** schedule.

## Why it's built this way

- **Nothing runs on a push to `main`.** A pull request has to be green to merge, so re-running the
  same matrix on the merge commit proves nothing and costs a machine. `nightly.yml` builds `main`
  after the merge instead.
- **Coverage does not gate a pull request.** It costs 36 minutes against 8.5 for the same tests
  uninstrumented, and about two hours on the release path — as a required check it was simply the
  slowest thing on the critical path, and a pull request sat waiting on a reporting metric.
  `coverage.yml` measures it daily, where it has the time and blocks nobody. That is the one cron
  in this folder, and it is deliberately off the hour.
- **Release calls CI via `workflow_call`.** Cutting a release re-runs the gate rather than trusting
  that somebody did, and it skips only the coverage leg.
- **`publish` waits on `nuget`.** A release tagged `v<version>` whose packages never reached
  nuget.org is a release in name only, and until that dependency existed it could happen silently.
- **`_build.yml` runs on windows-latest.** `Crush.Viewer` is WinForms (net10.0-windows), so
  cross-platform publish isn't possible for the viewer. `viewer-platforms.yml` covers the other two
  operating systems for the parts that do build there.
- **Viewer screenshots come from the viewer itself.** The workflow opens a generated PNG through the
  real decoder and uses the WinForms rendering path instead of brittle mouse-coordinate automation.
  Screenshot-only pushes are excluded from the screenshot workflow, so its own commit cannot loop.
- **`viewer-platforms.yml` triggers on push and not on pull request.** With both, every commit on a
  branch that had a pull request ran the whole matrix twice on the same sha. Collapsing the pair
  with a shared concurrency group was worse: the superseded run lands as a CANCELLED check, GitHub
  rolls the head up to FAILURE, and the merge box goes BLOCKED. Not scheduling the duplicate is the
  fix; cancelling it only moves the cost.
- **3-generation (GFS) retention**, not "keep last N". GFS guarantees at least one build per week
  for a month and one per month for a quarter.

## Scripts

### `version.pl`

Reads `<Version>X.Y.Z</Version>` from the first csproj at root / one level deep. Build number is
`git rev-list --count HEAD`.

```text
perl .github/workflows/scripts/version.pl          # 1.0.0.20
perl .github/workflows/scripts/version.pl --base   # 1.0.0
perl .github/workflows/scripts/version.pl --build  # 20
perl .github/workflows/scripts/version.pl --stamp  # writes X.Y.Z.BUILD into every csproj
```

### `update-changelog.mjs`

Buckets commit subjects into the release-notes body written to `CHANGES.md`. Commit-subject
convention: `+` Added, `*` Changed, `#` Fixed, `-` Removed, `!` TODO, anything else → Other.
`nightly.yml` passes `--notes-only`, so a nightly never writes a changelog file at all.

### `prune-nightlies.mjs`

GFS retention with `DAILY_KEEP=7`, `WEEKLY_KEEP=4`, `MONTHLY_KEEP=3`. Dry-run with `--dry-run`.

## Who maintains this

Every repo in the CompressionWorkbench / PNGCrushCS / AnythingToGif / ClaudeCodePortable family owns
its own copy. When changing it, prototype here then mirror the change to the siblings.

## Release artifacts

| Artifact                                            | Produced by         | What it is                          |
|-----------------------------------------------------|---------------------|-------------------------------------|
| `PNGCrushCS-cli-win-x64-<version>.zip`              | release + nightly   | Unified `crush.exe` CLI             |
| `PNGCrushCS-Viewer-win-x64-<version>.zip`           | release + nightly   | WinForms viewer                     |
| `Hawkynt.FileFormats.*` / `Hawkynt.ImageTransformUI`| release             | The three published NuGet packages  |
| Coverage HTML report                                 | coverage.yml        | Daily, not attached to a release    |
