using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Q0;

/// <summary>In-memory representation of a Q0 raw RGB format image.</summary>
public sealed class Q0File : IImageFileFormat<Q0File> {

  internal const int HeaderSize = 8;


  static string IImageFileFormat<Q0File>.PrimaryExtension => ".q0";
  static string[] IImageFileFormat<Q0File>.FileExtensions => [".q0"];
  static Q0File IImageFileFormat<Q0File>.FromFile(FileInfo file) => Q0Reader.FromFile(file);
  static Q0File IImageFileFormat<Q0File>.FromBytes(byte[] data) => Q0Reader.FromBytes(data);
  static Q0File IImageFileFormat<Q0File>.FromStream(Stream stream) => Q0Reader.FromStream(stream);
  static byte[] IImageFileFormat<Q0File>.ToBytes(Q0File file) => Q0Writer.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(Q0File file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static Q0File FromRawImage(RawImage image) {
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
