# Package README template

Package READMEs in this repository should look like members of one project, not documents that merely happen to live in the same tree.

Use this order for headings that apply to the package. Package-specific deep dives may be inserted after **Quick start** and before **Dependencies**.

```markdown
# Package.Name

[NuGet / CI / license / target-framework badges]

> One sentence stating what the package does and why a consumer would install it.

## 📦 Installation

## ✨ Features

## 🧩 Format / capability support

## 🚀 Quick start

## 📚 API / package-specific documentation

## 🏗️ Architecture

## 🔌 Dependencies

## ⚠️ Limitations

## ❤️ Support

## 📜 License
```

## Rules

- Use the emoji shown above for standard second-level headings. Do not invent a second emoji vocabulary for the same concepts.
- Keep common headings in the same order. Omit a heading only when it genuinely does not apply.
- Put installation before examples so NuGet readers can act immediately.
- Prefer capability matrices over prose such as “supports PNG, JPEG, GIF and …”. Use `✅`, `⚠️`, and `—` consistently for full, partial/limited, and unsupported capabilities.
- Link format names to a useful neutral overview when one exists (usually Wikipedia), and put the normative specification, original paper, or author's/project website in a separate **Reference** column.
- For standards, prefer the standards body or canonical project over third-party summaries: W3C/WHATWG, ITU, ISO project pages, IETF RFCs, SMPTE pages, Xiph, AOMedia, official author sites, etc.
- Large packages may keep a short curated matrix in the README and link to a complete generated or maintained coverage document. The README must still make the important capabilities obvious without requiring that extra click.
- Avoid exact coverage counts in marketing text unless they are generated. Prefer durable wording such as `850+` when the exact registry count changes frequently.
- Preserve valuable implementation notes, measurements, and format archaeology, but put them under package-specific sections rather than between Installation and Format support.
- Badges belong directly under the title. The one-line blockquote comes immediately after badges.
- End package READMEs with the standard Support and License sections used by the repository.

## Capability-table example

| Format | Extensions | Read | Write | Multi-image | Reference |
| --- | --- | :---: | :---: | :---: | --- |
| [PNG](https://en.wikipedia.org/wiki/PNG) | `.png` | ✅ | ✅ | — | [W3C PNG](https://www.w3.org/TR/png-3/) |
| [GIF](https://en.wikipedia.org/wiki/GIF) | `.gif` | ✅ | — | ✅ | [GIF89a](https://www.w3.org/Graphics/GIF/spec-gif89a.txt) |

## Standard footer

```markdown
## ❤️ Support

If this project saves you time or money, consider supporting its development:

[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsor-EA4AAA?logo=githubsponsors)](https://github.com/sponsors/Hawkynt)
[![PayPal](https://img.shields.io/badge/PayPal-Donate-00457C?logo=paypal)](https://www.paypal.me/hawkynt)

## 📜 License

Licensed under LGPL-3.0-or-later — see the repository [LICENSE](../LICENSE).
```

Adjust the relative `LICENSE` path for nested package directories.