namespace FileFormat.TextMode;

/// <summary>One screen cell: a code-page byte + foreground/background palette index (0-15) + blink flag.</summary>
public readonly record struct TextCell(byte CodePoint, byte Foreground, byte Background, bool Blink = false) {

  /// <summary>VGA text-mode attribute byte: blink (1) + bg (3) + fg (4) = 8 bits.</summary>
  public byte AttributeByte => (byte)((Blink ? 0x80 : 0) | ((Background & 0x07) << 4) | (Foreground & 0x0F));

  public static TextCell FromAttribute(byte codePoint, byte attribute, bool useBlinkBit = true) {
    var fg = (byte)(attribute & 0x0F);
    var bg = (byte)((attribute >> 4) & (useBlinkBit ? 0x07 : 0x0F));
    var blink = useBlinkBit && (attribute & 0x80) != 0;
    return new TextCell(codePoint, fg, bg, blink);
  }
}
