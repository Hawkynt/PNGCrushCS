# Agent guide — PNGCrushCS

Working agreement for **all** coding agents and human contributors working in
this repository. These rules are not optional. The full house spec lives in
the `Hawkynt/project-template` repo (`STANDARD.md`); this file is the
per-repo distillation.

## What this is

A **hybrid** C# image suite: NuGet packages (pure-managed image format
readers/writers, `Hawkynt.FileFormats.Images` meta-package) **and** a CLI
optimizer (`Crush.Image`) plus a viewer. Solution `PngCrush.slnx`; shared
build logic in `Directory.Build.props/.targets` and `build/`. Note the
**working-directory layout** documented in the README — sibling checkouts are
expected by relative csproj paths.

## Commits

- **Group changes semantically/logically** — one format/optimizer/concern per
  commit. Long-form diagnostic commit bodies (root cause → mechanism → fix)
  are the house style here — keep writing them.
- **Every subject line starts with a prefix**: `+` added · `-` removed ·
  `*` changed · `#` bug fixed · `!` critical todo.
- Never start a subject with "fix"/"bugfix"/"changed"/"modified".
- **No AI traces anywhere**: no `Co-Authored-By` AI lines, no "Generated
  with" footers, no agent mentions in messages, comments, or authorship.

## The loop (always, in this order)

1. **Before committing**: `dotnet build` + run the test suites under
   `Tests/` with the same category filters CI uses
   (`TestCategory!=Regression&TestCategory!=Performance` for the required
   tier). Wall-clock-sensitive tests get `[Category("Performance")]` — they
   run advisory, never blocking.
2. **Commit** (rules above) and **push**.
3. **Wait for CI**; on `main` a green CI triggers the nightly (prerelease +
   GFS prune, same-day replace). Fix and loop until everything is green.

Stable releases are **manual** (`gh workflow run release.yml`, semver
`v<version>` tags — this repo HAS a coordinated version) — never cut one
unless explicitly asked.

## Code conventions

- Latest C# features (records, pattern matching, primary ctors, spans);
  pure managed code — no native dependencies in the format libraries.
- Per-package folders with their own project files — untouched packages keep
  their version so `--skip-duplicate` re-uses the published artifact.
- Encoder/optimizer changes must never regress output size or speed without
  a stated reason; golden/regression tests guard the formats.

## README & repo conventions

- Standard frame: title → badges → one-line `>` blockquote; fixed emoji
  mapping for the standard sections (`## 🚀 CLI usage`,
  `## 🛠️ Build / test / run`, `## ❤️ Support`, `## 📜 License`).
- Package READMEs follow [`docs/PACKAGE_README_TEMPLATE.md`](docs/PACKAGE_README_TEMPLATE.md):
  common headings stay in one order, use the standard emoji vocabulary, and
  represent format/capability support with tables rather than prose.
- Format names in support tables should link to a useful overview; put the
  normative specification, original paper, or author's/project website in a
  separate Reference column whenever one exists.
- License is LGPL-3.0-or-later; the `## ❤️ Support` section and
  `.github/FUNDING.yml` stay intact.
