using System;
using FileFormat.Core;

namespace FileFormat.CmuWindowManager;

/// <summary>In-memory representation of a Carnegie Mellon University window-manager bitmap.</summary>
[FormatDetectionPriority(95)]
[FormatMagicBytes([0xF1, 0x00, 0x40, 0xBB])]
public readonly record struct CmuWindowManagerFile : IImageFormatReader<CmuWindowManagerFile>, IImageToRawImage<CmuWindowManagerFile>, IImageFromRawImage<CmuWindowManagerFile>, IImageFormatWriter<CmuWindowManagerFile> {

  /// <summary>Largest decoded image accepted by this managed implementation.</summary>
  public const int MaximumPixels = 100_000_000;

  static string IImageFormatMetadata<CmuWindowManagerFile>.PrimaryExtension => ".cmu";
  static string[] IImageFormatMetadata<CmuWindowManagerFile>.FileExtensions => [".cmu", ".cmuwm"];
  static CmuWindowManagerFile IImageFormatReader<CmuWindowManagerFile>.FromSpan(ReadOnlySpan<byte> data) => CmuWindowManagerReader.FromSpan(data);
  static byte[] IImageFormatWriter<CmuWindowManagerFile>.ToBytes(CmuWindowManagerFile file) => CmuWindowManagerWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bitmap depth. The historical CMU window-manager format uses depth 1.</summary>
  public ushort Depth { get; init; }

  /// <summary>
  /// Packed raster bytes, most-significant bit first within each byte. Zero represents black,
  /// one represents white, and unused row-end bits conventionally contain ones.
  /// </summary>
  public byte[] RasterData { get; init; }

  /// <summary>Gets the packed raster byte count for one row.</summary>
  public static int GetRowStride(int width) => checked((width + 7) >> 3);

  /// <summary>Converts a CMU window-manager bitmap to a platform-independent RGB24 image.</summary>
  public static RawImage ToRawImage(CmuWindowManagerFile file) {
    Validate(file, nameof(file));

    var rgb = new byte[checked(file.Width * file.Height * 3)];
    var stride = GetRowStride(file.Width);
    for (var y = 0; y < file.Height; ++y)
      for (var x = 0; x < file.Width; ++x) {
        var encoded = file.RasterData[y * stride + (x >> 3)];
        var white = ((encoded >> (7 - (x & 7))) & 1) != 0;
        var value = white ? (byte)255 : (byte)0;
        var destination = (y * file.Width + x) * 3;
        rgb[destination] = value;
        rgb[destination + 1] = value;
        rgb[destination + 2] = value;
      }

    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Creates a depth-1 CMU window-manager bitmap from an arbitrary image.</summary>
  public static CmuWindowManagerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    _ValidateDimensions(image.Width, image.Height, nameof(image));
    image = image.EnsureAnyFormat(PixelFormat.Rgb24);

    var stride = GetRowStride(image.Width);
    var raster = new byte[checked(stride * image.Height)];
    Array.Fill(raster, (byte)0xFF);

    for (var y = 0; y < image.Height; ++y)
      for (var x = 0; x < image.Width; ++x) {
        var source = (y * image.Width + x) * 3;
        var r = image.PixelData[source];
        var g = image.PixelData[source + 1];
        var b = image.PixelData[source + 2];
        var luma = (299 * r + 587 * g + 114 * b + 500) / 1000;
        if (luma >= 128)
          continue;

        raster[y * stride + (x >> 3)] &= (byte)~(1 << (7 - (x & 7)));
      }

    return new CmuWindowManagerFile {
      Width = image.Width,
      Height = image.Height,
      Depth = 1,
      RasterData = raster,
    };
  }

  internal static void Validate(CmuWindowManagerFile file, string parameterName) {
    _ValidateDimensions(file.Width, file.Height, parameterName);
    if (file.Depth != 1)
      throw new ArgumentOutOfRangeException(parameterName, $"CMU window-manager bitmap depth must be 1, got {file.Depth}.");

    var expected = checked(GetRowStride(file.Width) * file.Height);
    if (file.RasterData is null || file.RasterData.Length != expected)
      throw new ArgumentException($"CMU window-manager raster length must be exactly {expected} bytes.", parameterName);
  }

  internal static void ValidateDimensionsForRead(int width, int height) => _ValidateDimensions(width, height, "data");

  private static void _ValidateDimensions(int width, int height, string parameterName) {
    if (width <= 0)
      throw new ArgumentOutOfRangeException(parameterName, "CMU window-manager width must be positive.");
    if (height <= 0)
      throw new ArgumentOutOfRangeException(parameterName, "CMU window-manager height must be positive.");

    var pixelCount = (long)width * height;
    if (pixelCount > MaximumPixels)
      throw new ArgumentOutOfRangeException(parameterName, $"CMU window-manager image exceeds the {MaximumPixels:N0}-pixel implementation safety limit.");
  }
}
