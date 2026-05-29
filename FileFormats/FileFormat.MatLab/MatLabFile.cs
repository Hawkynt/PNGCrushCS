using System;
using FileFormat.Core;

namespace FileFormat.MatLab;

/// <summary>In-memory representation of a MATLAB Level 5 image image.</summary>
public readonly record struct MatLabFile : IImageFormatReader<MatLabFile>, IImageToRawImage<MatLabFile>, IImageFromRawImage<MatLabFile>, IImageFormatWriter<MatLabFile> {

  internal const int HeaderSize = 128;

  static string IImageFormatMetadata<MatLabFile>.PrimaryExtension => ".mat";
  static string[] IImageFormatMetadata<MatLabFile>.FileExtensions => [".mat"];
  static MatLabFile IImageFormatReader<MatLabFile>.FromSpan(ReadOnlySpan<byte> data) => MatLabReader.FromSpan(data);
  static byte[] IImageFormatWriter<MatLabFile>.ToBytes(MatLabFile file) => MatLabWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MatLabFile file) {
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
