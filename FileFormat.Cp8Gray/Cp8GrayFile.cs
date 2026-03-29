using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Cp8Gray;

/// <summary>In-memory representation of a CP8 grayscale image (headerless, square dimensions).</summary>
public sealed class Cp8GrayFile : IImageFileFormat<Cp8GrayFile> {

  static string IImageFileFormat<Cp8GrayFile>.PrimaryExtension => ".cp8";
  static string[] IImageFileFormat<Cp8GrayFile>.FileExtensions => [".cp8"];
  static Cp8GrayFile IImageFileFormat<Cp8GrayFile>.FromFile(FileInfo file) => Cp8GrayReader.FromFile(file);
  static Cp8GrayFile IImageFileFormat<Cp8GrayFile>.FromBytes(byte[] data) => Cp8GrayReader.FromBytes(data);
  static Cp8GrayFile IImageFileFormat<Cp8GrayFile>.FromStream(Stream stream) => Cp8GrayReader.FromStream(stream);
  static RawImage IImageFileFormat<Cp8GrayFile>.ToRawImage(Cp8GrayFile file) => ToRawImage(file);
  static byte[] IImageFileFormat<Cp8GrayFile>.ToBytes(Cp8GrayFile file) => Cp8GrayWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw 8-bit grayscale pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(Cp8GrayFile file) {
    ArgumentNullException.ThrowIfNull(file);
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

  public static Cp8GrayFile FromRawImage(RawImage image) => throw new NotSupportedException("CP8 writing from raw image is not supported.");
}
