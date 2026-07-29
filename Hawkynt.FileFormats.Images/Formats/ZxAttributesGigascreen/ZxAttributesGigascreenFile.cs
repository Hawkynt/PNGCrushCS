using System;
using FileFormat.Core;

namespace FileFormat.ZxAttributesGigascreen;

/// <summary>In-memory representation of a ZX Spectrum Attributes Gigascreen (.hlr) image.</summary>
/// <remarks>
/// A "gigascreen" swaps between two pictures every frame so the display averages them, buying
/// colours the hardware cannot show directly. This variant varies only the attributes: it stores
/// one 8-byte dither pattern and two full sets of colour attributes, and the perceived colour of
/// each cell is the mix of its two entries. The file opens with a short machine-code loader whose
/// first bytes readers match on.
/// </remarks>
public readonly record struct ZxAttributesGigascreenFile
  : IImageFormatReader<ZxAttributesGigascreenFile>, IImageToRawImage<ZxAttributesGigascreenFile>,
    IImageFromRawImage<ZxAttributesGigascreenFile>, IImageFormatWriter<ZxAttributesGigascreenFile> {

  /// <summary>The loader bytes readers check before accepting the file.</summary>
  public static ReadOnlySpan<byte> LoaderSignature => [118, 175, 211, 254, 33, 0, 88];

  /// <summary>Offset of the eight-byte dither pattern, one byte per scanline of a cell.</summary>
  public const int DitherOffset = 84;

  /// <summary>Offset of the first attribute set.</summary>
  public const int FirstAttributesOffset = 92;

  /// <summary>Attribute cells across the screen.</summary>
  public const int CellsAcross = ZxSpectrumGraphics.ScreenWidth / 8;

  /// <summary>Attribute cells down the screen.</summary>
  public const int CellsDown = ZxSpectrumGraphics.ScreenHeight / 8;

  /// <summary>Size of one attribute set.</summary>
  public const int AttributesSize = CellsAcross * CellsDown;

  /// <summary>Offset of the second attribute set.</summary>
  public const int SecondAttributesOffset = FirstAttributesOffset + AttributesSize;

  /// <summary>Total file size.</summary>
  public const int FileSize = SecondAttributesOffset + AttributesSize;

  static string IImageFormatMetadata<ZxAttributesGigascreenFile>.PrimaryExtension => ".hlr";
  static string[] IImageFormatMetadata<ZxAttributesGigascreenFile>.FileExtensions => [".hlr"];
  static ZxAttributesGigascreenFile IImageFormatReader<ZxAttributesGigascreenFile>.FromSpan(ReadOnlySpan<byte> data)
    => ZxAttributesGigascreenReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxAttributesGigascreenFile>.ToBytes(ZxAttributesGigascreenFile file)
    => ZxAttributesGigascreenWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxAttributesGigascreenFile>.VideoModes => [
    new("Attributes Gigascreen", [(ZxSpectrumGraphics.ScreenWidth, ZxSpectrumGraphics.ScreenHeight)], [256])
  ];

  /// <summary>Eight-byte dither, one row per scanline within a cell.</summary>
  public byte[] Dither { get; init; }

  /// <summary>Attributes shown on even frames.</summary>
  public byte[] FirstAttributes { get; init; }

  /// <summary>Attributes shown on odd frames.</summary>
  public byte[] SecondAttributes { get; init; }

  /// <summary>The dither we write: alternating pixels, so both colours of every cell contribute.</summary>
  private const byte _DEFAULT_DITHER = 0b10101010;

  public static RawImage ToRawImage(ZxAttributesGigascreenFile file) {
    const int width = ZxSpectrumGraphics.ScreenWidth;
    const int height = ZxSpectrumGraphics.ScreenHeight;

    var zx = ZxSpectrumGraphics.Palette;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var set = ((file.Dither[y & 7] >> (~x & 7)) & 1) != 0;
      var cell = (y >> 3) * CellsAcross + (x >> 3);
      var a = ZxSpectrumGraphics.ColorIndex(file.FirstAttributes[cell], set) * 3;
      var b = ZxSpectrumGraphics.ColorIndex(file.SecondAttributes[cell], set) * 3;

      // The display alternates the two sets, so what the eye sees is their average.
      var offset = (y * width + x) * 3;
      rgb[offset] = (byte)((zx[a] + zx[b]) / 2);
      rgb[offset + 1] = (byte)((zx[a + 1] + zx[b + 1]) / 2);
      rgb[offset + 2] = (byte)((zx[a + 2] + zx[b + 2]) / 2);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  public static ZxAttributesGigascreenFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != ZxSpectrumGraphics.ScreenWidth || image.Height != ZxSpectrumGraphics.ScreenHeight)
      throw new ArgumentException(
        $"Expected {ZxSpectrumGraphics.ScreenWidth}x{ZxSpectrumGraphics.ScreenHeight} but got {image.Width}x{image.Height}.",
        nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var zx = ZxSpectrumGraphics.Palette;

    var first = new byte[AttributesSize];
    var second = new byte[AttributesSize];

    for (var cellY = 0; cellY < CellsDown; ++cellY)
    for (var cellX = 0; cellX < CellsAcross; ++cellX) {
      // Average the cell, then pick the pair of palette entries whose own average is closest.
      long r = 0, g = 0, b = 0;
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var o = ((cellY * 8 + y) * ZxSpectrumGraphics.ScreenWidth + cellX * 8 + x) * 4;
        b += bgra.PixelData[o];
        g += bgra.PixelData[o + 1];
        r += bgra.PixelData[o + 2];
      }

      r /= 64; g /= 64; b /= 64;

      int bestI = 0, bestJ = 0;
      var bestDistance = long.MaxValue;
      for (var i = 0; i < ZxSpectrumGraphics.PaletteEntryCount; ++i)
      for (var j = i; j < ZxSpectrumGraphics.PaletteEntryCount; ++j) {
        long dr = (zx[i * 3] + zx[j * 3]) / 2 - r;
        long dg = (zx[i * 3 + 1] + zx[j * 3 + 1]) / 2 - g;
        long db = (zx[i * 3 + 2] + zx[j * 3 + 2]) / 2 - b;
        var distance = dr * dr + dg * dg + db * db;
        if (distance >= bestDistance)
          continue;

        bestDistance = distance;
        bestI = i;
        bestJ = j;
      }

      var cell = cellY * CellsAcross + cellX;
      first[cell] = ZxSpectrumGraphics.Attribute(bestI, bestI);
      second[cell] = ZxSpectrumGraphics.Attribute(bestJ, bestJ);
    }

    var dither = new byte[8];
    Array.Fill(dither, _DEFAULT_DITHER);

    return new() { Dither = dither, FirstAttributes = first, SecondAttributes = second };
  }
}
