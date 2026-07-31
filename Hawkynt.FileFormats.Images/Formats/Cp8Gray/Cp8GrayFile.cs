using System;
using FileFormat.Core;

namespace FileFormat.Cp8Gray;

/// <summary>In-memory representation of a CP8 grayscale image (headerless, square dimensions).</summary>
public readonly record struct Cp8GrayFile : IImageFormatReader<Cp8GrayFile>, IImageToRawImage<Cp8GrayFile>, IImageFromRawImage<Cp8GrayFile>, IImageFormatWriter<Cp8GrayFile> {

  static string IImageFormatMetadata<Cp8GrayFile>.PrimaryExtension => ".cp8";
  static string[] IImageFormatMetadata<Cp8GrayFile>.FileExtensions => [".cp8"];
  static Cp8GrayFile IImageFormatReader<Cp8GrayFile>.FromSpan(ReadOnlySpan<byte> data) => Cp8GrayReader.FromSpan(data);
  static byte[] IImageFormatWriter<Cp8GrayFile>.ToBytes(Cp8GrayFile file) => Cp8GrayWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw 8-bit grayscale pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(Cp8GrayFile file) {
    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var value = i < file.PixelData.Length ? file.PixelData[i] : (byte)0;
      rgb[i * 3] = value;
      rgb[i * 3 + 1] = value;
      rgb[i * 3 + 2] = value;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Builds a grayscale image from a picture.</summary>
  /// <remarks>
  /// The file has no header at all, so its dimensions are recovered from its length alone — which
  /// only works if the picture is square, and is why a CP8 always is. A picture that is not square
  /// is sampled up to a square of its longer side rather than cropped to its shorter one: that
  /// stretches the image, but it is the choice that keeps every row and column of the original.
  /// </remarks>
  public static Cp8GrayFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var side = Math.Max(image.Width, image.Height);
    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var gray = new byte[side * side];

    for (var y = 0; y < side; ++y) {
      var sourceY = image.Height == side ? y : y * image.Height / side;

      for (var x = 0; x < side; ++x) {
        var sourceX = image.Width == side ? x : x * image.Width / side;
        var source = (sourceY * image.Width + sourceX) * 3;

        var luminance = rgb.PixelData[source] * 77
                        + rgb.PixelData[source + 1] * 150
                        + rgb.PixelData[source + 2] * 29;
        gray[y * side + x] = (byte)(luminance >> 8);
      }
    }

    return new() { Width = side, Height = side, PixelData = gray };
  }

}
