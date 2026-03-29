using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffRgbn;

/// <summary>In-memory representation of an IFF RGBN (13-bit RGB + genlock) image.</summary>
[FormatMagicBytes([0x46, 0x4F, 0x52, 0x4D])]
public sealed class IffRgbnFile : IImageFileFormat<IffRgbnFile> {

  static string IImageFileFormat<IffRgbnFile>.PrimaryExtension => ".rgbn";
  static string[] IImageFileFormat<IffRgbnFile>.FileExtensions => [".rgbn", ".iff"];

  static bool? IImageFileFormat<IffRgbnFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D
      && header[8] == 0x52 && header[9] == 0x47 && header[10] == 0x42 && header[11] == 0x4E;

  static IffRgbnFile IImageFileFormat<IffRgbnFile>.FromFile(FileInfo file) => IffRgbnReader.FromFile(file);
  static IffRgbnFile IImageFileFormat<IffRgbnFile>.FromBytes(byte[] data) => IffRgbnReader.FromBytes(data);
  static IffRgbnFile IImageFileFormat<IffRgbnFile>.FromStream(Stream stream) => IffRgbnReader.FromStream(stream);
  static byte[] IImageFileFormat<IffRgbnFile>.ToBytes(IffRgbnFile file) => IffRgbnWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel: R, G, B).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this IFF RGBN file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IffRgbnFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates an <see cref="IffRgbnFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static IffRgbnFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
