using System;

namespace FileFormat.TextMode;

/// <summary>
/// Produces the byte buffers consumed by the <see cref="BitmapFontEmbedded"/> catalogue. Each
/// build method returns a raw <c>256 × cellHeight</c> glyph array suitable for deflate +
/// embedding. The buffers are <em>placeholder bitmaps</em>: cell size and weight differ per era,
/// but the underlying letter shapes all derive from <see cref="ProceduralVgaFont"/>'s 5×7
/// strokes. Replace any era's embedded .fnt.dfl with a deflate of an authentic ROM dump to get
/// pixel-perfect display.
/// </summary>
public static class EraFontGenerator {

  /// <summary>IBM VGA 8×16: the canonical procedural VGA font, unmodified.</summary>
  public static byte[] BuildIbmVga8x16() => ProceduralVgaFont.Build();

  /// <summary>IBM EGA 8×14: VGA 8×16 with the top + bottom rows trimmed to 8×14.</summary>
  public static byte[] BuildIbmEga8x14() {
    var vga = ProceduralVgaFont.Build();
    var ega = new byte[256 * 14];
    for (var cp = 0; cp < 256; ++cp) {
      // Drop row 0 and row 15 from each glyph (those rows are typically padding in 8×16).
      for (var r = 0; r < 14; ++r)
        ega[cp * 14 + r] = vga[cp * 16 + r + 1];
    }
    return ega;
  }

  /// <summary>IBM CGA 8×8: VGA 8×16 vertically halved by OR-ing pairs of adjacent rows.</summary>
  public static byte[] BuildIbmCga8x8() {
    var vga = ProceduralVgaFont.Build();
    var cga = new byte[256 * 8];
    for (var cp = 0; cp < 256; ++cp)
      for (var r = 0; r < 8; ++r)
        cga[cp * 8 + r] = (byte)(vga[cp * 16 + r * 2] | vga[cp * 16 + r * 2 + 1]);
    return cga;
  }

  /// <summary>Amiga Topaz 8×16: VGA 8×16 with a 1-pixel horizontal double-strike for boldness.</summary>
  public static byte[] BuildAmigaTopaz8x16() {
    var vga = ProceduralVgaFont.Build();
    var topaz = new byte[256 * 16];
    for (var i = 0; i < vga.Length; ++i) {
      var row = vga[i];
      // Stretch each lit pixel one column right — preserves shape but adds boldness.
      topaz[i] = (byte)(row | (row >> 1));
    }
    return topaz;
  }

  /// <summary>C64 PETSCII 8×8: CGA 8×8 with rounded corners (single-pixel inner clear at extreme corners).</summary>
  public static byte[] BuildC64Petscii8x8() {
    var cga = BuildIbmCga8x8();
    var petscii = new byte[256 * 8];
    Buffer.BlockCopy(cga, 0, petscii, 0, cga.Length);
    // Lightly soften the top-left and top-right pixels of every glyph row 0 to suggest C64's
    // distinctive curvature without authoring full letterforms.
    for (var cp = 0; cp < 256; ++cp) {
      var row0 = petscii[cp * 8];
      petscii[cp * 8] = (byte)(row0 & 0x7E); // clear leftmost + rightmost bits of top row
    }
    return petscii;
  }

  /// <summary>Atari ATASCII 8×8: CGA 8×8 with a sharper baseline (no row-1 softening).</summary>
  public static byte[] BuildAtariAtascii8x8() {
    var cga = BuildIbmCga8x8();
    var atascii = new byte[256 * 8];
    Buffer.BlockCopy(cga, 0, atascii, 0, cga.Length);
    // ATASCII's distinctive look has angular, fully-orthogonal strokes — keep CGA as-is but
    // trim row 7 to a single dot pattern (suggesting the Atari's flat baseline).
    for (var cp = 0; cp < 256; ++cp) {
      var row7 = atascii[cp * 8 + 7];
      atascii[cp * 8 + 7] = (byte)(row7 & 0xC3); // keep only bits 7, 6, 1, 0
    }
    return atascii;
  }
}
