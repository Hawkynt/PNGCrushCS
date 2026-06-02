using System;
using FileFormat.Core;
using FileFormat.TextMode;

namespace FileFormat.XBin;

/// <summary>
/// XBIN binary text-mode art. 11-byte header: "XBIN" magic + EOF (0x1A) + width (LE16) + height (LE16) +
/// font height (1 byte) + flags (1 byte). Flags bit 0 = palette present (48 bytes RGB), bit 1 = font present
/// (256 × font-height bytes), bit 2 = compressed (RLE), bit 3 = non-blink (16 bg colours), bit 4 = 512-char font.
/// Cells = width × height × (codepoint, attribute) pairs, optionally RLE-compressed.
/// </summary>
[FormatMagicBytes([(byte)'X', (byte)'B', (byte)'I', (byte)'N', 0x1A])]
public readonly record struct XBinFile : IImageFormatReader<XBinFile>, IImageFormatWriter<XBinFile>, IImageToRawImage<XBinFile>, IImageFromRawImage<XBinFile> {

  static string IImageFormatMetadata<XBinFile>.PrimaryExtension => ".xb";
  static string[] IImageFormatMetadata<XBinFile>.FileExtensions => [".xb", ".xbin"];
  static XBinFile IImageFormatReader<XBinFile>.FromSpan(ReadOnlySpan<byte> data) => XBinReader.FromSpan(data);
  static byte[] IImageFormatWriter<XBinFile>.ToBytes(XBinFile file) => XBinWriter.ToBytes(file);

  public int ColumnCount { get; init; }
  public int RowCount { get; init; }
  public int FontHeight { get; init; }
  public XBinFlags Flags { get; init; }
  public byte[]? Palette { get; init; }   // 48 bytes RGB if Flags.Palette
  public byte[]? Font { get; init; }      // 256×FontHeight bytes if Flags.Font
  public TextCell[] Cells { get; init; }

  public static RawImage ToRawImage(XBinFile file) {
    ArgumentNullException.ThrowIfNull(file.Cells);
    BitmapFont? font = null;
    if (file.Font is { Length: > 0 } && file.FontHeight > 0)
      font = BitmapFont.FromBytes(8, file.FontHeight, file.Font);
    var palette = file.Palette is { Length: 48 } ? _ScaleSixBitPalette(file.Palette) : null;
    var screen = new TextScreen {
      ColumnCount = file.ColumnCount,
      RowCount = file.RowCount,
      Cells = file.Cells,
      Palette = palette,
      Font = font,
    };
    var img = TextScreenRenderer.Render(screen, font);
    return new() { Width = img.Width, Height = img.Height, Format = PixelFormat.Rgb24, PixelData = img.PixelData };
  }

  public static XBinFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("XBIN quantizer expects PixelFormat.Rgb24 — convert first via PixelConverter.", nameof(image));
    var font = BitmapFont.Default;
    if (image.Width % font.CellWidth != 0 || image.Height % font.CellHeight != 0)
      throw new ArgumentException($"XBIN requires the source image to align to the {font.CellWidth}×{font.CellHeight} text-cell grid.", nameof(image));
    var cols = image.Width / font.CellWidth;
    var rows = image.Height / font.CellHeight;
    var screen = TextScreenQuantizer.FromRgb24(image.PixelData, image.Width, image.Height, cols, rows, font);
    return new XBinFile {
      ColumnCount = cols,
      RowCount = rows,
      FontHeight = font.CellHeight,
      Flags = XBinFlags.NonBlink,
      Cells = screen.Cells,
    };
  }

  // XBIN palettes are 6-bit-per-channel (legacy VGA DAC). Scale 0..63 → 0..255.
  private static byte[] _ScaleSixBitPalette(byte[] sixBit) {
    var rgb = new byte[48];
    for (var i = 0; i < 48; ++i) {
      var v = sixBit[i] & 0x3F;
      rgb[i] = (byte)((v << 2) | (v >> 4));
    }
    return rgb;
  }
}

[Flags]
public enum XBinFlags : byte {
  None        = 0,
  Palette     = 1 << 0,
  Font        = 1 << 1,
  Compressed  = 1 << 2,
  NonBlink    = 1 << 3,
  Font512     = 1 << 4,
}
