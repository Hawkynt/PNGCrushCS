# DjVu ZP codec provenance

`ZpDecoder.cs` and `ZpEncoder.cs` are managed adaptations of the `djvu-zp` crate from
[matyushkin/djvu-rs](https://github.com/matyushkin/djvu-rs), pinned at commit
`ae9b8434c077f6ecd65cec93f5ce8e167ddefa96`. That implementation is explicitly clean-room,
written from public DjVu v3 material, and is licensed under MIT. Its license is reproduced verbatim
in `DJVU_RS_LICENSE`.

The algorithm was independently cross-checked against the published ZP/Z-Coder material:

- Léon Bottou, Paul G. Howard and Yoshua Bengio, *The Z-Coder Adaptive Binary Coder*, DCC 1998.
- US Patent 6,476,740, which publishes the interval split, fast MPS test, adaptation and
  renormalization process used by the coder.
- The public DjVu v3 specification material referenced by `djvu-rs`.

`ZpTables.cs` contains the probability, threshold and state-transition values required by the DjVu
bitstream. These are interoperability constants defining the format's adaptive state machine rather
than expressive implementation code.

DjVuLibre is GPL-licensed and is therefore **not** an implementation source for these files. It may
be used as a behavioral oracle for interoperability tests, but no DjVuLibre comments, names,
structure or control flow are copied or translated into this implementation.
