# CI/CD Pipeline — PNGCrushCS

> Everything in this folder is the automated pipeline for this repository.
> Workflows live here, their helper scripts live in `scripts/`.

## What this does

Three workflows, one shared build block, three helper scripts:

| File                            | Trigger                             | Purpose                                 |
|---------------------------------|-------------------------------------|-----------------------------------------|
| `ci.yml`                        | push + PR + `workflow_call`         | Build + categorised test tiers + coverage |
| `release.yml`                   | tag push `v*` + manual dispatch     | Cut a GitHub Release from a tag         |
| `nightly.yml`                   | successful CI run on `main`/`master`| Publish `nightly-YYYY-MM-DD` prerelease |
| `_build.yml`                    | `workflow_call` (internal)          | Shared Crush.Viewer publish+zip block   |
| `scripts/version.pl`            | invoked by the workflows            | Compute `X.Y.Z.BUILD` + stamp csprojs   |
| `scripts/update-changelog.mjs`  | invoked by the workflows            | Bucketise commits into CHANGELOG.md     |
| `scripts/prune-nightlies.mjs`   | invoked by the workflows            | 3-gen (GFS) retention of nightlies      |

## How it works

```
                push / PR
                    │
                    ▼
            ┌───────────────┐
            │    ci.yml     │──► solution build + filtered tests on
            └───┬───────┬───┘    ubuntu + windows (+ coverage on ubuntu)
                │       │
    tag v* ─────┤       │  on success on main/master
                ▼       ▼
        ┌──────────┐  ┌─────────────┐
        │ release  │  │  nightly    │
        │  .yml    │  │   .yml      │
        └────┬─────┘  └─────┬───────┘
             │              │
             ▼              ▼
        (both call _build.yml)
             │              │
             ▼              ▼
     GH Release v1.2.3   nightly-YYYY-MM-DD (prerelease)
                                │
                                ▼
                       scripts/prune-nightlies.mjs
                       (GFS: 7 daily + 4 weekly + 3 monthly)
```

## Test tiers

~557 test projects under `Tests/`. CI filters by `TestCategory`:

| Category         | Runs on PR?       | Purpose                              |
|------------------|-------------------|--------------------------------------|
| _default_        | ✓ required        | Fast unit + format suites            |
| `Regression`     | excluded          | Heavy regression suite               |
| `Performance`    | excluded          | Microbenchmarks                      |

Running ~1100 projects × hundreds of tests each on every PR is impractical; the filter keeps PR latency reasonable while still covering the format libraries.

## What it's for

- Every PR is built and tested on ubuntu + windows before it can merge.
- Every merge to `main`/`master` produces a **tested** nightly prerelease.
- Every `v*` tag cuts a proper release with `Crush.Viewer` (win-x64 single-file, self-contained).
- Old nightlies are auto-pruned on a **Grandfather-Father-Son** schedule.

## Why it's built this way

- **No cron triggers.** The old `Build.yml` ran weekly against a hardcoded matrix of 11 per-format CLIs that no longer exist in the repo. Event-driven triggers only fire when something actually changed, and the matrix is anchored to what actually builds.
- **Release calls CI via `workflow_call`.** Tag pushes don't retrigger `on: push` workflows; calling ci.yml explicitly keeps tests and releases in lockstep.
- **Nightly builds from the `workflow_run` payload's SHA**, not branch tip — a nightly is always a build of code CI actually validated.
- **`_build.yml` runs on windows-latest.** `Crush.Viewer` is WinForms (net10.0-windows), so cross-platform publish isn't possible for the viewer.
- **Categorised tests.** `TestCategory=Regression` and `=Performance` are skipped on PR; they add latency disproportionate to their extra coverage.
- **3-generation (GFS) retention**, not "keep last N". GFS guarantees at least one build per week for a month and one per month for a quarter.

## Scripts

### `version.pl`

Reads `<Version>X.Y.Z</Version>` from the first csproj at root / one level deep. Build number is `git rev-list --count HEAD`.

```
perl .github/workflows/scripts/version.pl          # 1.0.0.20
perl .github/workflows/scripts/version.pl --base   # 1.0.0
perl .github/workflows/scripts/version.pl --build  # 20
perl .github/workflows/scripts/version.pl --stamp  # writes X.Y.Z.BUILD into every csproj
```

Replaces the old `UpdateVersions.pl`.

### `update-changelog.mjs`

Prepends a new section to `CHANGELOG.md`. Commit-subject convention: `+` Added, `*` Changed, `#` Fixed, `-` Removed, `!` TODO, anything else → Other.

### `prune-nightlies.mjs`

GFS retention with `DAILY_KEEP=7`, `WEEKLY_KEEP=4`, `MONTHLY_KEEP=3`. Dry-run with `--dry-run`.

## Who maintains this

Every repo in the CompressionWorkbench / PNGCrushCS / AnythingToGif / ClaudeCodePortable family owns its own copy. When changing it, prototype here then mirror the change to the siblings.

## Release artifacts

| Artifact                                            | Produced by         | What it is                         |
|-----------------------------------------------------|---------------------|------------------------------------|
| `PNGCrushCS-cli-win-x64-<version>.zip`              | release + nightly   | Unified `crush.exe` CLI            |
| `PNGCrushCS-Viewer-win-x64-<version>.zip`           | release + nightly   | WinForms viewer (500+ formats)     |
| Coverage HTML report                                 | ci.yml (coverage)   | —                                  |

