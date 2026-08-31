using System;
using FileFormat.Core;

namespace FileFormat.Zinc;

/// <summary>In-memory representation of a Zinc Interface Library (ZIL) monochrome bitmap.</summary>
public readonly record struct ZincFile : IImageFormatReader<ZincFile>, IImageToRawImage<ZincFile>, IImageFromRawImage<ZincFile>, IImageFormatWriter<ZincFile> {

  /// <summary>Largest dimension representable by the format's USHORT header values.</summary>
  public const int MaximumDimension = ushort.MaxValue;

  /// <summary>Implementation safety limit used before allocating decoded pixel buffers.</summary>
  public const int MaximumPixels = 100_000_000;

  static string IImageFormatMetadata<ZincFile>.PrimaryExtension => ".zinc";
  static string[] IImageFormatMetadata<ZincFile>.FileExtensions => [".zinc"];
  static ZincFile IImageFormatReader<ZincFile>.FromSpan(ReadOnlySpan<byte> data) => ZincReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZincFile>.ToBytes(ZincFile file) => ZincWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>C identifier used for the generated USHORT array.</summary>
  public string Name { get; init; }

  /// <summary>
  /// Monochrome raster words, padded to 16 pixels per row. Bit 15 is the leftmost pixel in each word;
  /// a set bit represents black.
  /// </summary>
  public ushort[] RasterWords { get; init; }

  /// <summary>Gets the number of 16-bit raster words in one row.</summary>
  public static int GetWordsPerRow(int width) => (width + 15) >> 4;

  /// <summary>Converts a Zinc bitmap to an indexed black-and-white image.</summary>
  public static RawImage ToRawImage(ZincFile file) {
    Validate(file, nameof(file));

    var pixels = new byte[checked(file.Width * file.Height)];
    var wordsPerRow = GetWordsPerRow(file.Width);
    for (var y = 0; y < file.Height; ++y)
      for (var x = 0; x < file.Width; ++x) {
        var word = file.RasterWords[y * wordsPerRow + (x >> 4)];
        pixels[y * file.Width + x] = (byte)((word >> (15 - (x & 15))) & 1);
      }

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [255, 255, 255, 0, 0, 0],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a monochrome Zinc bitmap from an arbitrary image.</summary>
  public static ZincFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    _ValidateDimensions(image.Width, image.Height, nameof(image));
    image = image.EnsureAnyFormat(PixelFormat.Rgb24);

    var wordsPerRow = GetWordsPerRow(image.Width);
    var words = new ushort[checked(wordsPerRow * image.Height)];
    for (var y = 0; y < image.Height; ++y)
      for (var x = 0; x < image.Width; ++x) {
        var pixel = (y * image.Width + x) * 3;
        var r = image.PixelData[pixel];
        var g = image.PixelData[pixel + 1];
        var b = image.PixelData[pixel + 2];
        var luma = (299 * r + 587 * g + 114 * b + 500) / 1000;
        if (luma >= 128)
          continue;

        words[y * wordsPerRow + (x >> 4)] |= (ushort)(1 << (15 - (x & 15)));
      }

    return new ZincFile {
      Width = image.Width,
      Height = image.Height,
      Name = "image",
      RasterWords = words,
    };
  }

  internal static void Validate(ZincFile file, string parameterName) {
    _ValidateDimensions(file.Width, file.Height, parameterName);
    var expected = checked(GetWordsPerRow(file.Width) * file.Height);
    if (file.RasterWords is null || file.RasterWords.Length != expected)
      throw new ArgumentException($"Zinc raster length must be exactly {expected} words.", parameterName);
  }

  private static void _ValidateDimensions(int width, int height, string parameterName) {
    if (width <= 0 || width > MaximumDimension)
      throw new ArgumentOutOfRangeException(parameterName, $"Zinc width must be in the range 1..{MaximumDimension}.");
    if (height <= 0 || height > MaximumDimension)
      throw new ArgumentOutOfRangeException(parameterName, $"Zinc height must be in the range 1..{MaximumDimension}.");

    var pixels = (long)width * height;
    if (pixels > MaximumPixels)
      throw new ArgumentOutOfRangeException(parameterName, $"Zinc image exceeds the {MaximumPixels:N0}-pixel implementation safety limit.");
  }
}
