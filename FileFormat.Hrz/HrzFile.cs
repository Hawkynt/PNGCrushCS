using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Hrz;

/// <summary>In-memory representation of a HRZ (slow-scan television) image.</summary>
public sealed class HrzFile : IImageFileFormat<HrzFile> {

  static string IImageFileFormat<HrzFile>.PrimaryExtension => ".hrz";
  static string[] IImageFileFormat<HrzFile>.FileExtensions => [".hrz"];
  static HrzFile IImageFileFormat<HrzFile>.FromFile(FileInfo file) => HrzReader.FromFile(file);
  static HrzFile IImageFileFormat<HrzFile>.FromBytes(byte[] data) => HrzReader.FromBytes(data);
  static HrzFile IImageFileFormat<HrzFile>.FromStream(Stream stream) => HrzReader.FromStream(stream);
  static byte[] IImageFileFormat<HrzFile>.ToBytes(HrzFile file) => HrzWriter.ToBytes(file);
  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 240.</summary>
  public int Height => 240;

  /// <summary>Raw RGB pixel data (3 bytes per pixel, 184320 bytes total).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(HrzFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static HrzFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));
    if (image.Width != 256 || image.Height != 240)
      throw new ArgumentException($"Expected 256x240 but got {image.Width}x{image.Height}.", nameof(image));

    return new() {
      PixelData = image.PixelData[..],
    };
  }
}
