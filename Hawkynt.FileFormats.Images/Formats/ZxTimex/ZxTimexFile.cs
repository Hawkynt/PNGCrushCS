using System;
using FileFormat.Core;

namespace FileFormat.ZxTimex;

/// <summary>In-memory representation of a Timex HiColor file (12288 bytes: 6144 bitmap + 6144 per-scanline-row extended attributes).</summary>
public readonly record struct ZxTimexFile
  : IImageFormatReader<ZxTimexFile>, IImageToRawImage<ZxTimexFile>,
    IImageFromRawImage<ZxTimexFile>, IImageFormatWriter<ZxTimexFile> {

  static string IImageFormatMetadata<ZxTimexFile>.PrimaryExtension => ".tmx";
  /// <summary>
  /// Also .scr, which is what every Timex hi-colour picture in the corpus is named.
  /// </summary>
  /// <remarks>
  /// Only .tmx was claimed, so none of the three was read although this reader decodes every one of
  /// them read at all. The extension is shared with the ordinary ZX screen at 6912 bytes and the
  /// Timex hi-res one at 12289, and the lengths tell them apart.
  /// </remarks>
  static string[] IImageFormatMetadata<ZxTimexFile>.FileExtensions => [".tmx", ".scr"];
  static ZxTimexFile IImageFormatReader<ZxTimexFile>.FromSpan(ReadOnlySpan<byte> data) => ZxTimexReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxTimexFile>.ToBytes(ZxTimexFile file) => ZxTimexWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxTimexFile>.VideoModes => [
    new("Default", [(256, 192)], [16])
  ];

  /// <summary>ZX Spectrum normal palette (bright=0).</summary>
  internal static readonly int[] NormalPalette = [
    0x000000, 0x0000CD, 0xCD0000, 0xCD00CD, 0x00CD00, 0x00CDCD, 0xCDCD00, 0xCDCDCD
  ];

  /// <summary>ZX Spectrum bright palette (bright=1).</summary>
  internal static readonly int[] BrightPalette = [
    0x000000, 0x0000FF, 0xFF0000, 0xFF00FF, 0x00FF00, 0x00FFFF, 0xFFFF00, 0xFFFFFF
  ];

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>6144 bytes of 1bpp bitmap data in linear row order.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>6144 bytes of per-scanline-row extended attribute data (32 per row, 192 rows).</summary>
  public byte[] AttributeData { get; init; }

  /// <summary>Converts this Timex HiColor screen to Rgb24.</summary>
  public static RawImage ToRawImage(ZxTimexFile file) {

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var bitValue = (file.BitmapData[byteIndex] >> bitPosition) & 1;

        // The attribute area is addressed the same way the display file is — as third, character row
        // within the third, and scanline within the character — and the reader hands it over exactly
        // as the file holds it, where it de-interleaves the bitmap on the way in. Read straight
        // through, as this used to, the colours land on the wrong rows and fewer than half the pixels
        // match what RECOIL and XnView draw.
        var attribute = file.AttributeData[ZxSpectrumGraphics.LineOffset(y) + x / 8];
        var bright = (attribute >> 6) & 1;
        var paper = (attribute >> 3) & 0x07;
        var ink = attribute & 0x07;

        var palette = bright == 1 ? BrightPalette : NormalPalette;
        var color = palette[bitValue == 1 ? ink : paper];

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Builds a Timex HiColor screen from a <see cref="RawImage"/>. Every pixel is mapped onto the
  /// Spectrum's 16-entry palette; within each 8x1 strip only the two most common colours survive, since
  /// the hardware allows just one ink and one paper colour (and a shared bright flag) per strip.</summary>
  public static ZxTimexFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.SampleTo(256, 192);

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, ZxSpectrumGraphics.Palette.ToArray());
    var bitmap = new byte[6144];
    var attributes = new byte[6144];
    const int cellsAcross = 32;

    Span<int> counts = stackalloc int[16];
    for (var y = 0; y < 192; ++y)
    for (var cellX = 0; cellX < cellsAcross; ++cellX) {
      counts.Clear();
      for (var x = 0; x < 8; ++x)
        ++counts[indexed.PixelData[y * 256 + cellX * 8 + x] & 15];

      var paper = 0;
      for (var c = 1; c < counts.Length; ++c)
        if (counts[c] > counts[paper])
          paper = c;

      var ink = paper == 0 ? 1 : 0;
      for (var c = 0; c < counts.Length; ++c)
        if (c != paper && counts[c] > counts[ink])
          ink = c;

      // The attribute area is addressed the way the display file is, and the reader hands it over
      // exactly as the file holds it. Written straight through, as this did, every colour lands on
      // the wrong scanline and the picture does not survive its own round trip.
      attributes[ZxSpectrumGraphics.LineOffset(y) + cellX] = ZxSpectrumGraphics.Attribute(ink, paper);

      byte rowByte = 0;
      for (var x = 0; x < 8; ++x) {
        var color = indexed.PixelData[y * 256 + cellX * 8 + x] & 15;
        if (color == ink)
          rowByte |= (byte)(0x80 >> x);
      }

      bitmap[y * 32 + cellX] = rowByte;
    }

    return new() { BitmapData = bitmap, AttributeData = attributes };
  }

}
