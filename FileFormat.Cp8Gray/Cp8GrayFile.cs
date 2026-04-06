using System;
using FileFormat.Core;

namespace FileFormat.Cp8Gray;

/// <summary>In-memory representation of a CP8 grayscale image (headerless, square dimensions).</summary>
public readonly record struct Cp8GrayFile : IImageFormatReader<Cp8GrayFile>, IImageToRawImage<Cp8GrayFile>, IImageFormatWriter<Cp8GrayFile> {

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

}
