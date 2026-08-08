using System;
using FileFormat.Core;

namespace FileFormat.ZxPaintbrush;

/// <summary>In-memory representation of a ZX Spectrum ZX-Paintbrush image (6912+ bytes: 6144 bitmap + 768 attributes + optional extra data).</summary>
[FormatDetectionPriority(200)]
public readonly record struct ZxPaintbrushFile
  : IImageFormatReader<ZxPaintbrushFile>, IImageToRawImage<ZxPaintbrushFile>,
    IImageFromRawImage<ZxPaintbrushFile>, IImageFormatWriter<ZxPaintbrushFile> {

  static string IImageFormatMetadata<ZxPaintbrushFile>.PrimaryExtension => ".zxp";
  static string[] IImageFormatMetadata<ZxPaintbrushFile>.FileExtensions => [".zxp"];
  static ZxPaintbrushFile IImageFormatReader<ZxPaintbrushFile>.FromSpan(ReadOnlySpan<byte> data) => ZxPaintbrushReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxPaintbrushFile>.ToBytes(ZxPaintbrushFile file) => ZxPaintbrushWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxPaintbrushFile>.VideoModes => [
    new("Default", [(256, 192)], [16])
  ];

  /// <summary>ZX Spectrum normal palette (bright=0): Black, Blue, Red, Magenta, Green, Cyan, Yellow, White.</summary>
  private static readonly int[] _NormalPalette = [
    0x000000, 0x0000CD, 0xCD0000, 0xCD00CD, 0x00CD00, 0x00CDCD, 0xCDCD00, 0xCDCDCD
  ];

  /// <summary>ZX Spectrum bright palette (bright=1).</summary>
  private static readonly int[] _BrightPalette = [
    0x000000, 0x0000FF, 0xFF0000, 0xFF00FF, 0x00FF00, 0x00FFFF, 0xFFFF00, 0xFFFFFF
  ];

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>6144 bytes of 1bpp bitmap data in linear row order (deinterleaved).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>768 bytes of attribute data, one per 8x8 cell (bit 7=flash, bit 6=bright, bits 5-3=paper, bits 2-0=ink).</summary>
  public byte[] AttributeData { get; init; }

  /// <summary>Optional extra data appended after the standard 6912 bytes.</summary>
  public byte[] ExtraData { get; init; }

  /// <summary>Converts this ZX-Paintbrush screen to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(ZxPaintbrushFile file) {

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var bitValue = (file.BitmapData[byteIndex] >> bitPosition) & 1;

        var cellX = x / 8;
        var cellY = y / 8;
        var attribute = file.AttributeData[cellY * 32 + cellX];
        var bright = (attribute >> 6) & 1;
        var paper = (attribute >> 3) & 0x07;
        var ink = attribute & 0x07;

        var palette = bright == 1 ? _BrightPalette : _NormalPalette;
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

  /// <summary>Builds a ZX-Paintbrush screen from a <see cref="RawImage"/>. Every pixel is mapped onto the
  /// Spectrum's 16-entry palette; within each 8x8 cell only the two most common colours survive, since
  /// the hardware allows just one ink and one paper colour (and a shared bright flag) per cell.</summary>
  public static ZxPaintbrushFile FromRawImage(RawImage image) {
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

    return new() { BitmapData = bitmap, AttributeData = attributes, ExtraData = [] };
  }

}
