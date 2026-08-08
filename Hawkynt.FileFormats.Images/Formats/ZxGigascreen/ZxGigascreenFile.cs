using System;
using FileFormat.Core;

namespace FileFormat.ZxGigascreen;

/// <summary>In-memory representation of a ZX Spectrum Gigascreen file (13824 bytes: two complete 6912-byte screens, averaged for more colors).</summary>
public readonly record struct ZxGigascreenFile
  : IImageFormatReader<ZxGigascreenFile>, IImageToRawImage<ZxGigascreenFile>,
    IImageFromRawImage<ZxGigascreenFile>, IImageFormatWriter<ZxGigascreenFile> {

  static string IImageFormatMetadata<ZxGigascreenFile>.PrimaryExtension => ".gsc";
  /// <summary>
  /// Also <c>.img</c>, which is the name the reference decoder knows these by.
  /// </summary>
  /// <remarks>
  /// Nothing is at risk in claiming so general a name: the reader takes 13824 bytes and no other
  /// length, that being two whole Spectrum screens, and the registry tries every format that claims
  /// an extension rather than only the first.
  /// </remarks>
  static string[] IImageFormatMetadata<ZxGigascreenFile>.FileExtensions => [".gsc", ".img"];
  static ZxGigascreenFile IImageFormatReader<ZxGigascreenFile>.FromSpan(ReadOnlySpan<byte> data) => ZxGigascreenReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxGigascreenFile>.ToBytes(ZxGigascreenFile file) => ZxGigascreenWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxGigascreenFile>.VideoModes => [
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

  /// <summary>Converts this gigascreen to Rgb24 by averaging two screens pixel by pixel.</summary>
  public static RawImage ToRawImage(ZxGigascreenFile file) {

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);

        // Screen 1
        var bit1 = (file.BitmapData1[byteIndex] >> bitPosition) & 1;
        var cellX = x / 8;
        var cellY = y / 8;
        var attr1 = file.AttributeData1[cellY * 32 + cellX];
        var bright1 = (attr1 >> 6) & 1;
        var paper1 = (attr1 >> 3) & 0x07;
        var ink1 = attr1 & 0x07;
        var pal1 = bright1 == 1 ? BrightPalette : NormalPalette;
        var color1 = pal1[bit1 == 1 ? ink1 : paper1];

        // Screen 2
        var bit2 = (file.BitmapData2[byteIndex] >> bitPosition) & 1;
        var attr2 = file.AttributeData2[cellY * 32 + cellX];
        var bright2 = (attr2 >> 6) & 1;
        var paper2 = (attr2 >> 3) & 0x07;
        var ink2 = attr2 & 0x07;
        var pal2 = bright2 == 1 ? BrightPalette : NormalPalette;
        var color2 = pal2[bit2 == 1 ? ink2 : paper2];

        // Average RGB values
        var r = (((color1 >> 16) & 0xFF) + ((color2 >> 16) & 0xFF)) / 2;
        var g = (((color1 >> 8) & 0xFF) + ((color2 >> 8) & 0xFF)) / 2;
        var b = ((color1 & 0xFF) + (color2 & 0xFF)) / 2;

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

  /// <summary>Builds a Gigascreen file from a <see cref="RawImage"/> by encoding the same screen twice.
  /// The format's extra colours come from blending two different screens together, but a single
  /// <see cref="RawImage"/> only ever supplies one picture — encoding it identically into both halves
  /// keeps the average exact instead of inventing a second, unrelated screen.</summary>
  public static ZxGigascreenFile FromRawImage(RawImage image) {
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

    var bitmap2 = (byte[])bitmap.Clone();
    var attributes2 = (byte[])attributes.Clone();
    return new() { BitmapData1 = bitmap, AttributeData1 = attributes, BitmapData2 = bitmap2, AttributeData2 = attributes2 };
  }

}
