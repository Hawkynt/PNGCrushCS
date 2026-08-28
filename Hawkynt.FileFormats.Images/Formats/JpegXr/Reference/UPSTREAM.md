# JPEG XR reference core attribution

The files in this directory are derived verbatim from `SharpAstro/Codecs`,
`src/SharpAstro.Jxr`, pinned at commit
`7cad99deda0e6c68f68e1c9c64d442c5b85d48a2` (2026-08-20).

SharpAstro.Jxr is a pure-managed C# port of Microsoft's JXRLib JPEG XR
reference implementation. The upstream repository dedicates the code to
the public domain under the Unlicense. Only the self-contained T.832 codec
core is imported here; SharpAstro's T.833/TIFF facade and generic codec
adapters are intentionally excluded because this repository supplies its
own JPEG XR container and RawImage integration.

Upstream: https://github.com/SharpAstro/Codecs
