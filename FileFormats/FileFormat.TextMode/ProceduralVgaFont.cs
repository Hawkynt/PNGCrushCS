using System;

namespace FileFormat.TextMode;

/// <summary>
/// Builds a recognisable 8×16 CP437 font from compact 5×7 stroke patterns + procedurally-drawn
/// box-drawing primitives. Output: 4096 bytes (256 glyphs × 16 rows, 1 byte per row, MSB-leftmost).
/// Not pixel-identical to the IBM VGA ROM, but covers everything needed for NFO/ANSI rendering.
/// </summary>
public static class ProceduralVgaFont {

  public static byte[] Build() {
    var data = new byte[256 * 16];

    // ===== Box-drawing characters (CP437 0xB3..0xDA) =====
    // Single-line + double-line lattice. Each one is drawn from one centre-row + one centre-column
    // selected per code-point. Lower nybble of the bitmask below: NESW connections; upper: 'D' bit
    // = double line. Code-points outside the table fall through to the bitmap section.
    _DrawBoxLattice(data);

    // ===== Shade blocks 0xB0..0xB2 =====
    _FillShade(data, 0xB0, 0b10000000_00100010_10001000_00100010_10001000_00100010_10001000_00100010UL); // 25% dotted
    _FillShade(data, 0xB1, 0b10101010_01010101_10101010_01010101_10101010_01010101_10101010_01010101UL); // 50% chequer
    _FillShade(data, 0xB2, 0b11011101_01110111_11011101_01110111_11011101_01110111_11011101_01110111UL); // 75% dotted

    // ===== Full + half blocks =====
    for (var r = 0; r < 16; ++r) data[0xDB * 16 + r] = 0xFF;                 // █ full
    for (var r = 8; r < 16; ++r) data[0xDC * 16 + r] = 0xFF;                 // ▄ lower half
    for (var r = 0; r < 16; ++r) data[0xDD * 16 + r] = 0xF0;                 // ▌ left half
    for (var r = 0; r < 16; ++r) data[0xDE * 16 + r] = 0x0F;                 // ▐ right half
    for (var r = 0; r < 8;  ++r) data[0xDF * 16 + r] = 0xFF;                 // ▀ upper half

    // ===== 0x20..0x7E: ASCII printables from compact stroke patterns =====
    foreach (var (cp, pattern) in _AsciiStrokes) _PaintGlyph(data, cp, pattern);

    // ===== 0x01..0x1F + 0x7F: leave blank (the C0 art glyphs are non-essential for NFO rendering) =====
    // ===== 0x80..0xFF that we didn't draw: leave blank (fallback) =====

    return data;
  }

  // Each glyph: 7 rows × 5 columns. '#' = lit pixel, ' ' = clear. Painted into the 8×16 cell rows 4..10
  // at columns 1..5 so the result has nice padding around it.
  private static void _PaintGlyph(byte[] data, byte codePoint, string pattern) {
    var lines = pattern.Split('\n');
    if (lines.Length != 7) throw new InvalidOperationException("Stroke pattern must be exactly 7 rows.");
    for (var row = 0; row < 7; ++row) {
      byte b = 0;
      var line = lines[row];
      for (var col = 0; col < 5 && col < line.Length; ++col)
        if (line[col] != ' ')
          b |= (byte)(1 << (6 - col)); // shift into bits 6..2 → leaves 1 px padding on each side
      data[codePoint * 16 + 4 + row] = b;
    }
  }

  private static void _FillShade(byte[] data, byte codePoint, ulong pattern) {
    // 64-bit pattern = 8 rows × 8 cols; duplicate top half for rows 8..15.
    for (var r = 0; r < 8; ++r) {
      var b = (byte)((pattern >> ((7 - r) * 8)) & 0xFF);
      data[codePoint * 16 + r] = b;
      data[codePoint * 16 + 8 + r] = b;
    }
  }

  // Code points → 5×7 stroke patterns. We hand-author the alphabet + digits + critical punctuation so
  // NFO files render legibly. Other ASCII falls through to "blank" (a hollow square fallback would
  // also be reasonable; blank keeps the output cleaner).
  private static readonly (byte cp, string pattern)[] _AsciiStrokes = [
    ((byte)' ', "     \n     \n     \n     \n     \n     \n     "),
    ((byte)'!', "  #  \n  #  \n  #  \n  #  \n     \n     \n  #  "),
    ((byte)'"', " # # \n # # \n     \n     \n     \n     \n     "),
    ((byte)'#', " # # \n#####\n # # \n#####\n # # \n     \n     "),
    ((byte)'$', "  ###\n# #  \n ### \n  # #\n###  \n  #  \n     "),
    ((byte)'%', "##  #\n## # \n   # \n  # #\n #  #\n#   #\n     "),
    ((byte)'&', " #   \n# #  \n # # \n#  # \n # # \n#  ##\n     "),
    ((byte)'\'',"  #  \n  #  \n     \n     \n     \n     \n     "),
    ((byte)'(', "   # \n  #  \n #   \n #   \n #   \n  #  \n   # "),
    ((byte)')', " #   \n  #  \n   # \n   # \n   # \n  #  \n #   "),
    ((byte)'*', "  #  \n# # #\n #### \n # # \n# # #\n     \n     "),
    ((byte)'+', "     \n  #  \n  #  \n#####\n  #  \n  #  \n     "),
    ((byte)',', "     \n     \n     \n     \n  #  \n  #  \n #   "),
    ((byte)'-', "     \n     \n     \n#####\n     \n     \n     "),
    ((byte)'.', "     \n     \n     \n     \n     \n  #  \n  #  "),
    ((byte)'/', "    #\n   # \n  #  \n  #  \n #   \n#    \n#    "),
    ((byte)'0', " ### \n#   #\n#  ##\n# # #\n##  #\n#   #\n ### "),
    ((byte)'1', "  #  \n ##  \n# #  \n  #  \n  #  \n  #  \n#####"),
    ((byte)'2', " ### \n#   #\n    #\n   # \n  #  \n #   \n#####"),
    ((byte)'3', " ### \n#   #\n    #\n  ## \n    #\n#   #\n ### "),
    ((byte)'4', "   # \n  ## \n # # \n#  # \n#####\n   # \n   # "),
    ((byte)'5', "#####\n#    \n#### \n    #\n    #\n#   #\n ### "),
    ((byte)'6', " ### \n#    \n#    \n#### \n#   #\n#   #\n ### "),
    ((byte)'7', "#####\n    #\n   # \n  #  \n  #  \n  #  \n  #  "),
    ((byte)'8', " ### \n#   #\n#   #\n ### \n#   #\n#   #\n ### "),
    ((byte)'9', " ### \n#   #\n#   #\n ####\n    #\n    #\n ### "),
    ((byte)':', "     \n  #  \n  #  \n     \n  #  \n  #  \n     "),
    ((byte)';', "     \n  #  \n  #  \n     \n  #  \n  #  \n #   "),
    ((byte)'<', "   # \n  #  \n #   \n#    \n #   \n  #  \n   # "),
    ((byte)'=', "     \n     \n#####\n     \n#####\n     \n     "),
    ((byte)'>', " #   \n  #  \n   # \n    #\n   # \n  #  \n #   "),
    ((byte)'?', " ### \n#   #\n   # \n  #  \n  #  \n     \n  #  "),
    ((byte)'@', " ### \n#   #\n# ###\n# # #\n# ###\n#    \n ### "),
    ((byte)'A', "  #  \n # # \n#   #\n#   #\n#####\n#   #\n#   #"),
    ((byte)'B', "#### \n#   #\n#   #\n#### \n#   #\n#   #\n#### "),
    ((byte)'C', " ### \n#   #\n#    \n#    \n#    \n#   #\n ### "),
    ((byte)'D', "###  \n# #  \n#  # \n#  # \n#  # \n# #  \n###  "),
    ((byte)'E', "#####\n#    \n#    \n###  \n#    \n#    \n#####"),
    ((byte)'F', "#####\n#    \n#    \n###  \n#    \n#    \n#    "),
    ((byte)'G', " ### \n#   #\n#    \n#  ##\n#   #\n#   #\n ### "),
    ((byte)'H', "#   #\n#   #\n#   #\n#####\n#   #\n#   #\n#   #"),
    ((byte)'I', " ### \n  #  \n  #  \n  #  \n  #  \n  #  \n ### "),
    ((byte)'J', "    #\n    #\n    #\n    #\n    #\n#   #\n ### "),
    ((byte)'K', "#   #\n#  # \n# #  \n##   \n# #  \n#  # \n#   #"),
    ((byte)'L', "#    \n#    \n#    \n#    \n#    \n#    \n#####"),
    ((byte)'M', "#   #\n## ##\n# # #\n# # #\n#   #\n#   #\n#   #"),
    ((byte)'N', "#   #\n##  #\n# # #\n# # #\n#  ##\n#   #\n#   #"),
    ((byte)'O', " ### \n#   #\n#   #\n#   #\n#   #\n#   #\n ### "),
    ((byte)'P', "#### \n#   #\n#   #\n#### \n#    \n#    \n#    "),
    ((byte)'Q', " ### \n#   #\n#   #\n#   #\n# # #\n#  # \n ## #"),
    ((byte)'R', "#### \n#   #\n#   #\n#### \n# #  \n#  # \n#   #"),
    ((byte)'S', " ####\n#    \n#    \n ### \n    #\n    #\n#### "),
    ((byte)'T', "#####\n  #  \n  #  \n  #  \n  #  \n  #  \n  #  "),
    ((byte)'U', "#   #\n#   #\n#   #\n#   #\n#   #\n#   #\n ### "),
    ((byte)'V', "#   #\n#   #\n#   #\n#   #\n#   #\n # # \n  #  "),
    ((byte)'W', "#   #\n#   #\n#   #\n# # #\n# # #\n## ##\n#   #"),
    ((byte)'X', "#   #\n#   #\n # # \n  #  \n # # \n#   #\n#   #"),
    ((byte)'Y', "#   #\n#   #\n # # \n  #  \n  #  \n  #  \n  #  "),
    ((byte)'Z', "#####\n    #\n   # \n  #  \n #   \n#    \n#####"),
    ((byte)'[', " ### \n #   \n #   \n #   \n #   \n #   \n ### "),
    ((byte)'\\',"#    \n#    \n #   \n  #  \n   # \n    #\n    #"),
    ((byte)']', " ### \n   # \n   # \n   # \n   # \n   # \n ### "),
    ((byte)'^', "  #  \n # # \n#   #\n     \n     \n     \n     "),
    ((byte)'_', "     \n     \n     \n     \n     \n     \n#####"),
    ((byte)'`', " #   \n  #  \n     \n     \n     \n     \n     "),
    ((byte)'a', "     \n     \n ### \n    #\n ####\n#   #\n ####"),
    ((byte)'b', "#    \n#    \n#### \n#   #\n#   #\n#   #\n#### "),
    ((byte)'c', "     \n     \n ### \n#    \n#    \n#   #\n ### "),
    ((byte)'d', "    #\n    #\n ####\n#   #\n#   #\n#   #\n ####"),
    ((byte)'e', "     \n     \n ### \n#   #\n#####\n#    \n ### "),
    ((byte)'f', "  ## \n #   \n###  \n #   \n #   \n #   \n #   "),
    ((byte)'g', "     \n ####\n#   #\n#   #\n ####\n    #\n ### "),
    ((byte)'h', "#    \n#    \n#### \n#   #\n#   #\n#   #\n#   #"),
    ((byte)'i', "  #  \n     \n ##  \n  #  \n  #  \n  #  \n ### "),
    ((byte)'j', "   # \n     \n  ## \n   # \n   # \n#  # \n ##  "),
    ((byte)'k', "#    \n#    \n#  # \n# #  \n##   \n# #  \n#  # "),
    ((byte)'l', " ##  \n  #  \n  #  \n  #  \n  #  \n  #  \n ### "),
    ((byte)'m', "     \n     \n## # \n# # #\n# # #\n#   #\n#   #"),
    ((byte)'n', "     \n     \n#### \n#   #\n#   #\n#   #\n#   #"),
    ((byte)'o', "     \n     \n ### \n#   #\n#   #\n#   #\n ### "),
    ((byte)'p', "     \n     \n#### \n#   #\n#### \n#    \n#    "),
    ((byte)'q', "     \n     \n ####\n#   #\n ####\n    #\n    #"),
    ((byte)'r', "     \n     \n# ## \n##  #\n#    \n#    \n#    "),
    ((byte)'s', "     \n     \n ####\n#    \n ### \n    #\n#### "),
    ((byte)'t', " #   \n #   \n###  \n #   \n #   \n #   \n  ## "),
    ((byte)'u', "     \n     \n#   #\n#   #\n#   #\n#   #\n ####"),
    ((byte)'v', "     \n     \n#   #\n#   #\n#   #\n # # \n  #  "),
    ((byte)'w', "     \n     \n#   #\n#   #\n# # #\n# # #\n ## #"),
    ((byte)'x', "     \n     \n#   #\n # # \n  #  \n # # \n#   #"),
    ((byte)'y', "     \n     \n#   #\n#   #\n ####\n    #\n#### "),
    ((byte)'z', "     \n     \n#####\n   # \n  #  \n #   \n#####"),
    ((byte)'{', "  ## \n #   \n #   \n#    \n #   \n #   \n  ## "),
    ((byte)'|', "  #  \n  #  \n  #  \n  #  \n  #  \n  #  \n  #  "),
    ((byte)'}', " ##  \n   # \n   # \n    #\n   # \n   # \n ##  "),
    ((byte)'~', " #  #\n# # #\n#  # \n     \n     \n     \n     "),
  ];

  private static void _DrawBoxLattice(byte[] data) {
    // Single-line connections (light box drawing).
    _PaintBox(data, 0xC4, h: true,  v: false, e: 0, w: 0);              // ─
    _PaintBox(data, 0xB3, h: false, v: true,  e: 0, w: 0);              // │
    _PaintBox(data, 0xDA, h: true,  v: true,  topOnly: false, bottomOnly: true, leftOnly: false, rightOnly: true);  // ┌
    _PaintBox(data, 0xBF, h: true,  v: true,  topOnly: false, bottomOnly: true, leftOnly: true,  rightOnly: false); // ┐
    _PaintBox(data, 0xC0, h: true,  v: true,  topOnly: true,  bottomOnly: false, leftOnly: false, rightOnly: true); // └
    _PaintBox(data, 0xD9, h: true,  v: true,  topOnly: true,  bottomOnly: false, leftOnly: true,  rightOnly: false); // ┘
    _PaintBox(data, 0xC3, h: true,  v: true,  rightOnly: true);          // ├
    _PaintBox(data, 0xB4, h: true,  v: true,  leftOnly: true);           // ┤
    _PaintBox(data, 0xC2, h: true,  v: true,  bottomOnly: true);         // ┬
    _PaintBox(data, 0xC1, h: true,  v: true,  topOnly: true);            // ┴
    _PaintBox(data, 0xC5, h: true,  v: true);                            // ┼
    // Double-line connections — we draw as two parallel single-lines.
    _PaintDoubleBox(data, 0xCD, horiz: true);   // ═
    _PaintDoubleBox(data, 0xBA, horiz: false);  // ║
    _PaintDoubleBox(data, 0xC9, leftTop: (true, true), rightBottom: (false, false), corner: 0xC9);  // ╔
    _PaintDoubleBox(data, 0xBB, leftTop: (true, true), rightBottom: (false, false), corner: 0xBB);  // ╗
    _PaintDoubleBox(data, 0xC8, leftTop: (true, true), rightBottom: (false, false), corner: 0xC8);  // ╚
    _PaintDoubleBox(data, 0xBC, leftTop: (true, true), rightBottom: (false, false), corner: 0xBC);  // ╝
  }

  // h/v: extend the horizontal/vertical centre stroke through the full row/column.
  // topOnly etc.: also only paint the corresponding half so we can make corners.
  private static void _PaintBox(byte[] data, byte cp, bool h, bool v,
                                int e = 0, int w = 0,
                                bool topOnly = false, bool bottomOnly = false,
                                bool leftOnly = false, bool rightOnly = false) {
    const int midCol = 4;     // bit 3 = column 4 (8-wide cell, 0-indexed columns 0..7)
    const int midRow = 7;     // row 7 = middle of 16 rows
    var bit = (byte)(1 << (7 - midCol));

    if (h) {
      // horizontal centre line
      var leftStart = (topOnly || bottomOnly || rightOnly) ? midCol : 0;
      var rightEnd  = (topOnly || bottomOnly || leftOnly)  ? midCol : 7;
      byte hb = 0;
      for (var c = leftStart; c <= rightEnd; ++c) hb |= (byte)(1 << (7 - c));
      data[cp * 16 + midRow] = hb;
    }
    if (v) {
      var topStart    = (leftOnly || rightOnly || bottomOnly) ? midRow : 0;
      var bottomEnd   = (leftOnly || rightOnly || topOnly)    ? midRow : 15;
      for (var r = topStart; r <= bottomEnd; ++r) data[cp * 16 + r] |= bit;
    }
  }

  private static void _PaintDoubleBox(byte[] data, byte cp,
                                       bool horiz = false,
                                       (bool h, bool v) leftTop = default,
                                       (bool h, bool v) rightBottom = default,
                                       byte corner = 0) {
    // ═ → two horizontal rows just above and below centre.
    // ║ → two vertical columns just left and right of centre.
    // For corner glyphs (╔╗╚╝) we draw an L shape made of two parallel strokes.
    if (horiz) {
      byte b = 0xFF;
      data[cp * 16 + 6] = b;
      data[cp * 16 + 8] = b;
      return;
    }
    if (cp == 0xBA /* ║ */) {
      for (var r = 0; r < 16; ++r) data[cp * 16 + r] = 0b00010100; // bits at cols 3 and 5
      return;
    }
    // Corner ╔: rows 6-7 horizontal from col 3 rightward, cols 3 and 5 vertical from row 6 downward.
    // We compose by writing the two strokes per glyph orientation.
    switch (cp) {
      case 0xC9: // ╔ top-left
        for (var c = 3; c <= 7; ++c) { data[cp * 16 + 6] |= (byte)(1 << (7 - c)); data[cp * 16 + 8] |= (byte)(1 << (7 - c)); }
        for (var r = 6; r < 16; ++r) { data[cp * 16 + r] |= 1 << (7 - 3); data[cp * 16 + r] |= 1 << (7 - 5); }
        // Clear the inner bits to make a proper "L" with double walls.
        data[cp * 16 + 7] &= 0b00010100;
        break;
      case 0xBB: // ╗ top-right
        for (var c = 0; c <= 5; ++c) { data[cp * 16 + 6] |= (byte)(1 << (7 - c)); data[cp * 16 + 8] |= (byte)(1 << (7 - c)); }
        for (var r = 6; r < 16; ++r) { data[cp * 16 + r] |= 1 << (7 - 3); data[cp * 16 + r] |= 1 << (7 - 5); }
        data[cp * 16 + 7] &= 0b00010100;
        break;
      case 0xC8: // ╚ bottom-left
        for (var c = 3; c <= 7; ++c) { data[cp * 16 + 7] |= (byte)(1 << (7 - c)); data[cp * 16 + 9] |= (byte)(1 << (7 - c)); }
        for (var r = 0; r <= 9; ++r) { data[cp * 16 + r] |= 1 << (7 - 3); data[cp * 16 + r] |= 1 << (7 - 5); }
        data[cp * 16 + 8] &= 0b00010100;
        break;
      case 0xBC: // ╝ bottom-right
        for (var c = 0; c <= 5; ++c) { data[cp * 16 + 7] |= (byte)(1 << (7 - c)); data[cp * 16 + 9] |= (byte)(1 << (7 - c)); }
        for (var r = 0; r <= 9; ++r) { data[cp * 16 + r] |= 1 << (7 - 3); data[cp * 16 + r] |= 1 << (7 - 5); }
        data[cp * 16 + 8] &= 0b00010100;
        break;
    }
  }
}
