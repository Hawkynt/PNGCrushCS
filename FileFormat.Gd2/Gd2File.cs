using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Gd2;

/// <summary>In-memory representation of a libgd GD2 image.</summary>
[FormatMagicBytes([0x67, 0x64, 0x32, 0x00])]
public sealed class Gd2File : IImageFileFormat<Gd2File> {

  static string IImageFileFormat<Gd2File>.PrimaryExtension => ".gd2";
  static string[] IImageFileFormat<Gd2File>.FileExtensions => [".gd2"];
  static Gd2File IImageFileFormat<Gd2File>.FromFile(FileInfo file) => Gd2Reader.FromFile(file);
  static Gd2File IImageFileFormat<Gd2File>.FromBytes(byte[] data) => Gd2Reader.FromBytes(data);
  static Gd2File IImageFileFormat<Gd2File>.FromStream(Stream stream) => Gd2Reader.FromStream(stream);
  static RawImage IImageFileFormat<Gd2File>.ToRawImage(Gd2File file) => ToRawImage(file);
  static byte[] IImageFileFormat<Gd2File>.ToBytes(Gd2File file) => Gd2Writer.ToBytes(file);

  /// <summary>The 4-byte GD2 signature: "gd2\0" (0x67 0x64 0x32 0x00).</summary>
  public static ReadOnlySpan<byte> Signature => [0x67, 0x64, 0x32, 0x00];

  /// <summary>The size of the GD2 header in bytes.</summary>
  public const int HeaderSize = 18;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>GD2 format version (typically 2).</summary>
  public int Version { get; init; } = 2;

  /// <summary>Chunk size used for tiled storage.</summary>
  public int ChunkSize { get; init; }

  /// <summary>Format code (1=raw, 2=compressed).</summary>
  public int Format { get; init; } = 1;

  /// <summary>Raw ARGB pixel data in GD2 byte order (4 bytes per pixel, big-endian, 7-bit alpha: 0=opaque, 127=transparent).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(Gd2File file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var rgba = new byte[pixelCount * 4];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 4;
      var dstOffset = i * 4;
      var gd2Alpha = file.PixelData[srcOffset];
      var r = file.PixelData[srcOffset + 1];
      var g = file.PixelData[srcOffset + 2];
      var b = file.PixelData[srcOffset + 3];

      // GD2 alpha: 0=opaque, 127=transparent -> standard alpha: 255=opaque, 0=transparent
      var alpha = (byte)(255 - gd2Alpha * 2);
      if (gd2Alpha == 0)
        alpha = 255;
      else if (gd2Alpha >= 127)
        alpha = 0;

      rgba[dstOffset] = r;
      rgba[dstOffset + 1] = g;
      rgba[dstOffset + 2] = b;
      rgba[dstOffset + 3] = alpha;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba32,
      PixelData = rgba,
    };
  }

  public static Gd2File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgba32)
      throw new ArgumentException($"Expected {PixelFormat.Rgba32} but got {image.Format}.", nameof(image));

    var pixelCount = image.Width * image.Height;
    var gd2Pixels = new byte[pixelCount * 4];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 4;
      var dstOffset = i * 4;
      var r = image.PixelData[srcOffset];
      var g = image.PixelData[srcOffset + 1];
      var b = image.PixelData[srcOffset + 2];
      var a = image.PixelData[srcOffset + 3];

      // Standard alpha: 255=opaque, 0=transparent -> GD2 alpha: 0=opaque, 127=transparent
      var gd2Alpha = (byte)((255 - a) / 2);

      gd2Pixels[dstOffset] = gd2Alpha;
      gd2Pixels[dstOffset + 1] = r;
      gd2Pixels[dstOffset + 2] = g;
      gd2Pixels[dstOffset + 3] = b;
    }

    var chunkSize = Math.Max(image.Width, image.Height);
    return new() {
      Width = image.Width,
      Height = image.Height,
      ChunkSize = chunkSize,
      PixelData = gd2Pixels,
    };
  }
}
