using System;
using FileFormat.Core;

namespace FileFormat.BennetYeeFace;

/// <summary>In-memory representation of a Bennet Yee Face (.ybm) monochrome bitmap image.</summary>
public readonly record struct BennetYeeFaceFile : IImageFormatReader<BennetYeeFaceFile>, IImageToRawImage<BennetYeeFaceFile>, IImageFromRawImage<BennetYeeFaceFile>, IImageFormatWriter<BennetYeeFaceFile> {

  static string IImageFormatMetadata<BennetYeeFaceFile>.PrimaryExtension => ".ybm";
  static string[] IImageFormatMetadata<BennetYeeFaceFile>.FileExtensions => [".ybm"];
  static BennetYeeFaceFile IImageFormatReader<BennetYeeFaceFile>.FromSpan(ReadOnlySpan<byte> data) => BennetYeeFaceReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<BennetYeeFaceFile>.Capabilities => FormatCapability.MonochromeOnly | FormatCapability.VariableResolution;
  static byte[] IImageFormatWriter<BennetYeeFaceFile>.ToBytes(BennetYeeFaceFile file) => BennetYeeFaceWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, rows padded to 16-bit (word) boundary.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Computes the row stride: ((width + 15) / 16) * 2 bytes.</summary>
  internal static int ComputeStride(int width) => ((width + 15) / 16) * 2;

  public static RawImage ToRawImage(BennetYeeFaceFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static BennetYeeFaceFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    var srcStride = (image.Width + 7) / 8;
    var dstStride = ComputeStride(image.Width);

    byte[] pixelData;
    if (srcStride == dstStride) {
      pixelData = image.PixelData[..];
    } else {
      pixelData = new byte[dstStride * image.Height];
      for (var y = 0; y < image.Height; ++y)
        image.PixelData.AsSpan(y * srcStride, srcStride).CopyTo(pixelData.AsSpan(y * dstStride));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixelData,
    };
  }
}
