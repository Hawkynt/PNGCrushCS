using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.BennetYeeFace;

/// <summary>In-memory representation of a Bennet Yee Face (.ybm) monochrome bitmap image.</summary>
public sealed class BennetYeeFaceFile : IImageFileFormat<BennetYeeFaceFile> {

  static string IImageFileFormat<BennetYeeFaceFile>.PrimaryExtension => ".ybm";
  static string[] IImageFileFormat<BennetYeeFaceFile>.FileExtensions => [".ybm"];
  static FormatCapability IImageFileFormat<BennetYeeFaceFile>.Capabilities => FormatCapability.MonochromeOnly | FormatCapability.VariableResolution;
  static BennetYeeFaceFile IImageFileFormat<BennetYeeFaceFile>.FromFile(FileInfo file) => BennetYeeFaceReader.FromFile(file);
  static BennetYeeFaceFile IImageFileFormat<BennetYeeFaceFile>.FromBytes(byte[] data) => BennetYeeFaceReader.FromBytes(data);
  static BennetYeeFaceFile IImageFileFormat<BennetYeeFaceFile>.FromStream(Stream stream) => BennetYeeFaceReader.FromStream(stream);
  static RawImage IImageFileFormat<BennetYeeFaceFile>.ToRawImage(BennetYeeFaceFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<BennetYeeFaceFile>.ToBytes(BennetYeeFaceFile file) => BennetYeeFaceWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, rows padded to 16-bit (word) boundary.</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Computes the row stride: ((width + 15) / 16) * 2 bytes.</summary>
  internal static int ComputeStride(int width) => ((width + 15) / 16) * 2;

  public RawImage ToRawImage() => new() {
    Width = this.Width,
    Height = this.Height,
    Format = PixelFormat.Indexed1,
    PixelData = this.PixelData[..],
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
