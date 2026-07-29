using System;
using FileFormat.Core;

namespace FileFormat.ZxAttributes;

/// <summary>In-memory representation of a ZX Spectrum attribute-only (.atr) file.</summary>
/// <remarks>
/// The 768 bytes of colour attributes from a Spectrum screen, with no bitmap. Each byte covers an
/// 8x8 cell and names an ink colour, a paper colour and a bright flag. Viewers show the attributes
/// over a fixed dither so both colours of every cell stay visible — without one the file would
/// render as flat paper.
/// </remarks>
public readonly record struct ZxAttributesFile
  : IImageFormatReader<ZxAttributesFile>, IImageToRawImage<ZxAttributesFile>,
    IImageFromRawImage<ZxAttributesFile>, IImageFormatWriter<ZxAttributesFile> {

  /// <summary>Attribute cells across the screen.</summary>
  public const int CellsAcross = ZxSpectrumGraphics.ScreenWidth / 8;

  /// <summary>Attribute cells down the screen.</summary>
  public const int CellsDown = ZxSpectrumGraphics.ScreenHeight / 8;

  /// <summary>Total file size.</summary>
  public const int FileSize = CellsAcross * CellsDown;

  static string IImageFormatMetadata<ZxAttributesFile>.PrimaryExtension => ".atr";
  static string[] IImageFormatMetadata<ZxAttributesFile>.FileExtensions => [".atr"];
  static ZxAttributesFile IImageFormatReader<ZxAttributesFile>.FromSpan(ReadOnlySpan<byte> data)
    => ZxAttributesReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxAttributesFile>.ToBytes(ZxAttributesFile file)
    => ZxAttributesWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxAttributesFile>.VideoModes => [
    new("Attributes", [(ZxSpectrumGraphics.ScreenWidth, ZxSpectrumGraphics.ScreenHeight)],
        [ZxSpectrumGraphics.PaletteEntryCount])
  ];

  /// <summary>One attribute byte per 8x8 cell.</summary>
  public byte[] AttributeData { get; init; }

  /// <summary>The dither a viewer paints under the attributes so both cell colours show.</summary>
  private static bool _InkAt(int x, int y) => ((x ^ y) & 1) != 0;

  public static RawImage ToRawImage(ZxAttributesFile file) {
    const int width = ZxSpectrumGraphics.ScreenWidth;
    const int height = ZxSpectrumGraphics.ScreenHeight;

    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var attribute = file.AttributeData[(y >> 3) * CellsAcross + (x >> 3)];
      pixels[y * width + x] = (byte)ZxSpectrumGraphics.ColorIndex(attribute, _InkAt(x, y));
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = ZxSpectrumGraphics.Palette.ToArray(),
      PaletteCount = ZxSpectrumGraphics.PaletteEntryCount,
    };
  }

  public static ZxAttributesFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != ZxSpectrumGraphics.ScreenWidth || image.Height != ZxSpectrumGraphics.ScreenHeight)
      throw new ArgumentException(
        $"Expected {ZxSpectrumGraphics.ScreenWidth}x{ZxSpectrumGraphics.ScreenHeight} but got {image.Width}x{image.Height}.",
        nameof(image));

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, ZxSpectrumGraphics.Palette.ToArray());
    var attributes = new byte[FileSize];

    // Only the two dominant colours of each cell survive; there is nowhere else to put the rest.
    Span<int> counts = stackalloc int[ZxSpectrumGraphics.PaletteEntryCount];
    for (var cellY = 0; cellY < CellsDown; ++cellY)
    for (var cellX = 0; cellX < CellsAcross; ++cellX) {
      counts.Clear();
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        ++counts[indexed.PixelData[(cellY * 8 + y) * ZxSpectrumGraphics.ScreenWidth + cellX * 8 + x] & 15];

      int paper = 0, ink = 0;
      for (var c = 1; c < counts.Length; ++c)
        if (counts[c] > counts[paper])
          paper = c;
      for (var c = 0; c < counts.Length; ++c)
        if (c != paper && counts[c] > counts[ink == paper ? paper : ink])
          ink = c;

      attributes[cellY * CellsAcross + cellX] = ZxSpectrumGraphics.Attribute(ink, paper);
    }

    return new() { AttributeData = attributes };
  }
}
