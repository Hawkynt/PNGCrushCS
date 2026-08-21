namespace FileFormat.Codecs.FlashSv2;

/// <summary>
/// The 128-entry colour table Screen Video v2's hybrid pixel coding indexes into whenever a packet
/// does not carry a palette of its own.
/// </summary>
/// <remarks>
/// Transcribed from Appendix C of the SWF File Format Specification, "Screen Video v2 Palette", which
/// prints it as a C array of thirty-two-bit <c>0x00rrggbb</c> values — the specification's own words for
/// it are "In the absence of a valid palette, the code will fall back on the following 128-entry RGB
/// palette", so this table is what every stream this decoder was measured against actually uses: none
/// of them carries a palette of its own.
/// </remarks>
internal static class FlashSv2Palette {

  /// <summary>128 entries, each packed as <c>0x00RRGGBB</c>, in the order the specification prints them.</summary>
  public static readonly uint[] Default = [
    0x000000, 0x333333, 0x666666, 0x999999, 0xCCCCCC, 0xFFFFFF,
    0x330000, 0x660000, 0x990000, 0xCC0000, 0xFF0000, 0x003300,
    0x006600, 0x009900, 0x00CC00, 0x00FF00, 0x000033, 0x000066,
    0x000099, 0x0000CC, 0x0000FF, 0x333300, 0x666600, 0x999900,
    0xCCCC00, 0xFFFF00, 0x003333, 0x006666, 0x009999, 0x00CCCC,
    0x00FFFF, 0x330033, 0x660066, 0x990099, 0xCC00CC, 0xFF00FF,
    0xFFFF33, 0xFFFF66, 0xFFFF99, 0xFFFFCC, 0xFF33FF, 0xFF66FF,
    0xFF99FF, 0xFFCCFF, 0x33FFFF, 0x66FFFF, 0x99FFFF, 0xCCFFFF,
    0xCCCC33, 0xCCCC66, 0xCCCC99, 0xCCCCFF, 0xCC33CC, 0xCC66CC,
    0xCC99CC, 0xCCFFCC, 0x33CCCC, 0x66CCCC, 0x99CCCC, 0xFFCCCC,
    0x999933, 0x999966, 0x9999CC, 0x9999FF, 0x993399, 0x996699,
    0x99CC99, 0x99FF99, 0x339999, 0x669999, 0xCC9999, 0xFF9999,
    0x666633, 0x666699, 0x6666CC, 0x6666FF, 0x663366, 0x669966,
    0x66CC66, 0x66FF66, 0x336666, 0x996666, 0xCC6666, 0xFF6666,
    0x333366, 0x333399, 0x3333CC, 0x3333FF, 0x336633, 0x339933,
    0x33CC33, 0x33FF33, 0x663333, 0x993333, 0xCC3333, 0xFF3333,
    0x003366, 0x336600, 0x660033, 0x006633, 0x330066, 0x663300,
    0x336699, 0x669933, 0x993366, 0x339966, 0x663399, 0x996633,
    0x6699CC, 0x99CC66, 0xCC6699, 0x66CC99, 0x9966CC, 0xCC9966,
    0x99CCFF, 0xCCFF99, 0xFF99CC, 0x99FFCC, 0xCC99FF, 0xFFCC99,
    0x111111, 0x222222, 0x444444, 0x555555, 0xAAAAAA, 0xBBBBBB,
    0xDDDDDD, 0xEEEEEE,
  ];

  /// <summary>Builds a 128-entry, three-byte-a-colour B, G, R table from <see cref="Default"/> — the
  /// same channel order every 24-bit picture in this decoder is stored in.</summary>
  public static byte[] DefaultBgr() {
    var table = new byte[Default.Length * 3];
    for (var i = 0; i < Default.Length; ++i) {
      var rgb = Default[i];
      table[i * 3] = (byte)rgb;
      table[i * 3 + 1] = (byte)(rgb >> 8);
      table[i * 3 + 2] = (byte)(rgb >> 16);
    }

    return table;
  }
}
