namespace FileFormat.Core;

/// <summary>The 256-colour palette a VGA starts with, for the formats that store none of their own.</summary>
/// <remarks>
/// Several DOS-era formats — BSAVE screen dumps, Fastgraph pixel runs — hold nothing but indices. The
/// colours those indices stood for were loaded into the card by the program that drew the picture and
/// were never part of the file, so a reader has nothing to go on but what the card holds when nobody
/// has changed it: the sixteen EGA colours, sixteen greys, and a spread of the rest.
/// <para/>
/// This is a synthesis of that default rather than a dump of a particular BIOS, and it is offered so
/// that an indexed picture has somewhere to start from — not as a claim about what the picture looked
/// like. Where the real colours matter they have to come from beside the file, which is what the
/// <see cref="PaletteSidecar"/> convention is for.
/// </remarks>
public static class VgaPalette {

  /// <summary>The standard sixteen EGA colours, three bytes each.</summary>
  private static readonly byte[] _Ega = [
    0x00, 0x00, 0x00, 0x00, 0x00, 0xAA, 0x00, 0xAA, 0x00, 0x00, 0xAA, 0xAA,
    0xAA, 0x00, 0x00, 0xAA, 0x00, 0xAA, 0xAA, 0x55, 0x00, 0xAA, 0xAA, 0xAA,
    0x55, 0x55, 0x55, 0x55, 0x55, 0xFF, 0x55, 0xFF, 0x55, 0x55, 0xFF, 0xFF,
    0xFF, 0x55, 0x55, 0xFF, 0x55, 0xFF, 0xFF, 0xFF, 0x55, 0xFF, 0xFF, 0xFF,
  ];

  private static readonly byte[] _Default256 = _Build();

  /// <summary>256 RGB triplets: 0..15 the EGA colours, 16..31 a grey ramp, the rest a colour cube.</summary>
  public static byte[] Default256 => _Default256;

  private static byte[] _Build() {
    var palette = new byte[256 * 3];
    _Ega.CopyTo(palette, 0);

    for (var i = 0; i < 16; ++i) {
      var value = (byte)(i * 255 / 15);
      var at = (16 + i) * 3;
      palette[at] = value;
      palette[at + 1] = value;
      palette[at + 2] = value;
    }

    var index = 32;
    for (var r = 0; r < 6 && index < 256; ++r)
    for (var g = 0; g < 6 && index < 256; ++g)
    for (var b = 0; b < 6 && index < 256; ++b, ++index) {
      var at = index * 3;
      palette[at] = (byte)(r * 51);
      palette[at + 1] = (byte)(g * 51);
      palette[at + 2] = (byte)(b * 51);
    }

    for (; index < 256; ++index) {
      var value = (byte)((index - 248) * 32);
      var at = index * 3;
      palette[at] = value;
      palette[at + 1] = value;
      palette[at + 2] = value;
    }

    return palette;
  }
}
