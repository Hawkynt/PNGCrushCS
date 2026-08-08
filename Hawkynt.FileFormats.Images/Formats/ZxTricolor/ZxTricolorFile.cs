using System;
using FileFormat.Core;

namespace FileFormat.ZxTricolor;

/// <summary>In-memory representation of a ZX Spectrum Tricolor file (20736 bytes: three complete 6912-byte screens, interlaced for more colors).</summary>
public readonly record struct ZxTricolorFile
  : IImageFormatReader<ZxTricolorFile>, IImageToRawImage<ZxTricolorFile>,
    IImageFromRawImage<ZxTricolorFile>, IImageFormatWriter<ZxTricolorFile> {

  static string IImageFormatMetadata<ZxTricolorFile>.PrimaryExtension => ".3cl";
  static string[] IImageFormatMetadata<ZxTricolorFile>.FileExtensions => [".3cl"];
  static ZxTricolorFile IImageFormatReader<ZxTricolorFile>.FromSpan(ReadOnlySpan<byte> data) => ZxTricolorReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxTricolorFile>.ToBytes(ZxTricolorFile file) => ZxTricolorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxTricolorFile>.VideoModes => [
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

  /// <summary>Screen 1: 6144 bytes bitmap data in linear row order.</summary>
  public byte[] BitmapData1 { get; init; }

  /// <summary>Screen 1: 768 bytes attribute data.</summary>
  public byte[] AttributeData1 { get; init; }

  /// <summary>Screen 2: 6144 bytes bitmap data in linear row order.</summary>
  public byte[] BitmapData2 { get; init; }

  /// <summary>Screen 2: 768 bytes attribute data.</summary>
  public byte[] AttributeData2 { get; init; }

  /// <summary>Screen 3: 6144 bytes bitmap data in linear row order.</summary>
  public byte[] BitmapData3 { get; init; }

  /// <summary>Screen 3: 768 bytes attribute data.</summary>
  public byte[] AttributeData3 { get; init; }

  /// <summary>Converts this tricolor screen to Rgb24 by averaging all three screens pixel by pixel.</summary>
  public static RawImage ToRawImage(ZxTricolorFile file) {

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var cellX = x / 8;
        var cellY = y / 8;
        var attrIndex = cellY * 32 + cellX;

        var color1 = _GetPixelColor(file.BitmapData1, file.AttributeData1, byteIndex, bitPosition, attrIndex);
        var color2 = _GetPixelColor(file.BitmapData2, file.AttributeData2, byteIndex, bitPosition, attrIndex);
        var color3 = _GetPixelColor(file.BitmapData3, file.AttributeData3, byteIndex, bitPosition, attrIndex);

        var r = (((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF) + ((color3 >> 16) & 0xFF)) / 3;
        var g = (((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF) + ((color3 >> 8) & 0xFF)) / 3;
        var b = ((color1 & 0xFF) + (color2 & 0xFF) + (color3 & 0xFF)) / 3;

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)r;
        rgb[offset + 1] = (byte)g;
        rgb[offset + 2] = (byte)b;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  private static int _GetPixelColor(byte[] bitmap, byte[] attributes, int byteIndex, int bitPosition, int attrIndex) {
    var bitValue = (bitmap[byteIndex] >> bitPosition) & 1;
    var attribute = attributes[attrIndex];
    var bright = (attribute >> 6) & 1;
    var paper = (attribute >> 3) & 0x07;
    var ink = attribute & 0x07;
    var palette = bright == 1 ? BrightPalette : NormalPalette;
    return palette[bitValue == 1 ? ink : paper];
  }

  /// <summary>Builds a Tricolor file from a <see cref="RawImage"/> by encoding the same screen three times.
  /// The format's extra colours come from interlacing three different screens together, but a single
  /// <see cref="RawImage"/> only ever supplies one picture — encoding it identically into all three keeps
  /// the average exact instead of inventing two unrelated screens.</summary>
  public static ZxTricolorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.SampleTo(256, 192);

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, ZxSpectrumGraphics.Palette.ToArray());
    var bitmap = new byte[6144];
    var attributes = new byte[768];
    const int cellsAcross = 32, cellsDown = 24;

    Span<int> counts = stackalloc int[16];
    for (var cellY = 0; cellY < cellsDown; ++cellY)
    for (var cellX = 0; cellX < cellsAcross; ++cellX) {
      counts.Clear();
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        ++counts[indexed.PixelData[(cellY * 8 + y) * 256 + cellX * 8 + x] & 15];

      var paper = 0;
      for (var c = 1; c < counts.Length; ++c)
        if (counts[c] > counts[paper])
          paper = c;

      var ink = paper == 0 ? 1 : 0;
      for (var c = 0; c < counts.Length; ++c)
        if (c != paper && counts[c] > counts[ink])
          ink = c;

      attributes[cellY * cellsAcross + cellX] = ZxSpectrumGraphics.Attribute(ink, paper);

      for (var y = 0; y < 8; ++y) {
        byte rowByte = 0;
        for (var x = 0; x < 8; ++x) {
          var color = indexed.PixelData[(cellY * 8 + y) * 256 + cellX * 8 + x] & 15;
          if (color == ink)
            rowByte |= (byte)(0x80 >> x);
        }

        bitmap[(cellY * 8 + y) * 32 + cellX] = rowByte;
      }
    }

    return new() {
      BitmapData1 = bitmap, AttributeData1 = attributes,
      BitmapData2 = (byte[])bitmap.Clone(), AttributeData2 = (byte[])attributes.Clone(),
      BitmapData3 = (byte[])bitmap.Clone(), AttributeData3 = (byte[])attributes.Clone(),
    };
  }

}
