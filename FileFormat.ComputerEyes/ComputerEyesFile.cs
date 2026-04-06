using System;
using FileFormat.Core;

namespace FileFormat.ComputerEyes;

/// <summary>In-memory representation of a ComputerEyes grayscale image.</summary>
public readonly record struct ComputerEyesFile : IImageFormatReader<ComputerEyesFile>, IImageToRawImage<ComputerEyesFile>, IImageFormatWriter<ComputerEyesFile> {

  /// <summary>Header size: 2 width + 2 height = 4 bytes.</summary>
  public const int HeaderSize = 4;

  static string IImageFormatMetadata<ComputerEyesFile>.PrimaryExtension => ".ce";
  static string[] IImageFormatMetadata<ComputerEyesFile>.FileExtensions => [".ce", ".ce1", ".ce2"];
  static ComputerEyesFile IImageFormatReader<ComputerEyesFile>.FromSpan(ReadOnlySpan<byte> data) => ComputerEyesReader.FromSpan(data);
  static byte[] IImageFormatWriter<ComputerEyesFile>.ToBytes(ComputerEyesFile file) => ComputerEyesWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw 8-bit grayscale pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(ComputerEyesFile file) {
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
