using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MatLab;

/// <summary>In-memory representation of a MATLAB Level 5 image image.</summary>
public sealed class MatLabFile : IImageFileFormat<MatLabFile> {

  internal const int HeaderSize = 128;


  static string IImageFileFormat<MatLabFile>.PrimaryExtension => ".mat";
  static string[] IImageFileFormat<MatLabFile>.FileExtensions => [".mat"];
  static MatLabFile IImageFileFormat<MatLabFile>.FromFile(FileInfo file) => MatLabReader.FromFile(file);
  static MatLabFile IImageFileFormat<MatLabFile>.FromBytes(byte[] data) => MatLabReader.FromBytes(data);
  static MatLabFile IImageFileFormat<MatLabFile>.FromStream(Stream stream) => MatLabReader.FromStream(stream);
  static byte[] IImageFileFormat<MatLabFile>.ToBytes(MatLabFile file) => MatLabWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(MatLabFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static MatLabFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
