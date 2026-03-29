using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ComputerEyes;

/// <summary>In-memory representation of a ComputerEyes grayscale image.</summary>
public sealed class ComputerEyesFile : IImageFileFormat<ComputerEyesFile> {

  /// <summary>Header size: 2 width + 2 height = 4 bytes.</summary>
  public const int HeaderSize = 4;

  static string IImageFileFormat<ComputerEyesFile>.PrimaryExtension => ".ce";
  static string[] IImageFileFormat<ComputerEyesFile>.FileExtensions => [".ce", ".ce1", ".ce2"];
  static ComputerEyesFile IImageFileFormat<ComputerEyesFile>.FromFile(FileInfo file) => ComputerEyesReader.FromFile(file);
  static ComputerEyesFile IImageFileFormat<ComputerEyesFile>.FromBytes(byte[] data) => ComputerEyesReader.FromBytes(data);
  static ComputerEyesFile IImageFileFormat<ComputerEyesFile>.FromStream(Stream stream) => ComputerEyesReader.FromStream(stream);
  static RawImage IImageFileFormat<ComputerEyesFile>.ToRawImage(ComputerEyesFile file) => ToRawImage(file);
  static byte[] IImageFileFormat<ComputerEyesFile>.ToBytes(ComputerEyesFile file) => ComputerEyesWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw 8-bit grayscale pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(ComputerEyesFile file) {
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

  public static ComputerEyesFile FromRawImage(RawImage image) => throw new NotSupportedException("ComputerEyes writing from raw image is not supported.");
}
