using System;
using FileFormat.Core;

namespace FileFormat.ZxBorderMulticolor;

/// <summary>In-memory representation of a ZX Spectrum Border Multicolor 8x4 file (11904 bytes: 6144 bitmap + 1536 attributes + 4224 border data).</summary>
public readonly record struct ZxBorderMulticolorFile
  : IImageFormatReader<ZxBorderMulticolorFile>, IImageToRawImage<ZxBorderMulticolorFile>,
    IImageFromRawImage<ZxBorderMulticolorFile>, IImageFormatWriter<ZxBorderMulticolorFile> {

  static string IImageFormatMetadata<ZxBorderMulticolorFile>.PrimaryExtension => ".bmc4";
  static string[] IImageFormatMetadata<ZxBorderMulticolorFile>.FileExtensions => [".bmc4"];
  static ZxBorderMulticolorFile IImageFormatReader<ZxBorderMulticolorFile>.FromSpan(ReadOnlySpan<byte> data) => ZxBorderMulticolorReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxBorderMulticolorFile>.ToBytes(ZxBorderMulticolorFile file) => ZxBorderMulticolorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxBorderMulticolorFile>.VideoModes => [
    new("Default", [(256, 192)], [16])
  ];

  /// <summary>ZX Spectrum normal palette (bright=0): Black, Blue, Red, Magenta, Green, Cyan, Yellow, White.</summary>
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

  /// <summary>6144 bytes of 1bpp bitmap data in linear row order (deinterleaved).</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>1536 bytes of 8x4 attribute data (each attribute covers 8 pixels wide by 4 pixels tall).
  /// Bit 7=flash, bit 6=bright, bits 5-3=paper, bits 2-0=ink.</summary>
  public byte[] AttributeData { get; init; }

  /// <summary>4224 bytes of border color data.</summary>
  public byte[] BorderData { get; init; }

  /// <summary>Converts this ZX Spectrum Border Multicolor 8x4 image to a platform-independent <see cref="RawImage"/> in Rgb24 format (256x192).</summary>
  public static RawImage ToRawImage(ZxBorderMulticolorFile file) {

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var bitValue = (file.BitmapData[byteIndex] >> bitPosition) & 1;

        // 8x4 cells: 32 columns, 48 rows of attribute cells
        var cellX = x / 8;
        var cellY = y / 4;
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

  /// <summary>Builds a Border Multicolor screen from a <see cref="RawImage"/>. Colours are mapped onto
  /// the Spectrum's 16-entry palette; within each 8x4 cell only the two most common colours survive,
  /// since the hardware allows just one ink and one paper colour per cell. Border data (drawn outside
  /// the 256x192 picture) carries no information a <see cref="RawImage"/> can supply, so it comes back zeroed.</summary>
  public static ZxBorderMulticolorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.SampleTo(256, 192);

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, ZxSpectrumGraphics.Palette.ToArray());
    var bitmap = new byte[6144];
    var attributes = new byte[1536];
    const int cellsAcross = 32, cellsDown = 48, cellHeight = 4;

    Span<int> counts = stackalloc int[16];
    for (var cellY = 0; cellY < cellsDown; ++cellY)
    for (var cellX = 0; cellX < cellsAcross; ++cellX) {
      counts.Clear();
      for (var y = 0; y < cellHeight; ++y)
      for (var x = 0; x < 8; ++x)
        ++counts[indexed.PixelData[(cellY * cellHeight + y) * 256 + cellX * 8 + x] & 15];

      var paper = 0;
      for (var c = 1; c < counts.Length; ++c)
        if (counts[c] > counts[paper])
          paper = c;

      var ink = paper == 0 ? 1 : 0;
      for (var c = 0; c < counts.Length; ++c)
        if (c != paper && counts[c] > counts[ink])
          ink = c;

      attributes[cellY * cellsAcross + cellX] = ZxSpectrumGraphics.Attribute(ink, paper);

      for (var y = 0; y < cellHeight; ++y) {
        byte rowByte = 0;
        for (var x = 0; x < 8; ++x) {
          var color = indexed.PixelData[(cellY * cellHeight + y) * 256 + cellX * 8 + x] & 15;
          if (color == ink)
            rowByte |= (byte)(0x80 >> x);
        }

        bitmap[(cellY * cellHeight + y) * 32 + cellX] = rowByte;
      }
    }

    return new() { BitmapData = bitmap, AttributeData = attributes, BorderData = new byte[4224] };
  }

}
