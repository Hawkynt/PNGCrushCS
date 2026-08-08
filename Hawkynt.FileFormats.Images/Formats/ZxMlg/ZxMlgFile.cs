using System;
using FileFormat.Core;

namespace FileFormat.ZxMlg;

/// <summary>In-memory representation of a ZX Spectrum MLG editor file (6912 bytes: 6144 bitmap + 768 attributes).</summary>
public readonly record struct ZxMlgFile
  : IImageFormatReader<ZxMlgFile>, IImageToRawImage<ZxMlgFile>,
    IImageFromRawImage<ZxMlgFile>, IImageFormatWriter<ZxMlgFile> {

  static string IImageFormatMetadata<ZxMlgFile>.PrimaryExtension => ".mlg";
  static string[] IImageFormatMetadata<ZxMlgFile>.FileExtensions => [".mlg"];
  static ZxMlgFile IImageFormatReader<ZxMlgFile>.FromSpan(ReadOnlySpan<byte> data) => ZxMlgReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxMlgFile>.ToBytes(ZxMlgFile file) => ZxMlgWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxMlgFile>.VideoModes => [
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

  /// <summary>768 bytes of attribute data, one per 8x8 cell.</summary>
  public byte[] AttributeData { get; init; }

  /// <summary>Converts this MLG screen to Rgb24.</summary>
  public static RawImage ToRawImage(ZxMlgFile file) {

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

  /// <summary>Builds an MLG screen from a <see cref="RawImage"/>. Every pixel is mapped onto the
  /// Spectrum's 16-entry palette; within each 8x8 cell only the two most common colours survive, since
  /// the hardware allows just one ink and one paper colour (and a shared bright flag) per cell.</summary>
  public static ZxMlgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != 256 || image.Height != 192)
      throw new ArgumentException($"ZX Spectrum MLG screens are always 256x192, but got {image.Width}x{image.Height}.", nameof(image));

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

    return new() { BitmapData = bitmap, AttributeData = attributes };
  }

}
