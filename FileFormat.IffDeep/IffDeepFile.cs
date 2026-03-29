using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffDeep;

/// <summary>In-memory representation of an IFF DEEP (Deep Paint) image.</summary>
[FormatMagicBytes([0x46, 0x4F, 0x52, 0x4D])]
public sealed class IffDeepFile : IImageFileFormat<IffDeepFile> {

  static string IImageFileFormat<IffDeepFile>.PrimaryExtension => ".deep";
  static string[] IImageFileFormat<IffDeepFile>.FileExtensions => [".deep", ".iff"];

  static bool? IImageFileFormat<IffDeepFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x4D
      && header[8] == 0x44 && header[9] == 0x45 && header[10] == 0x45 && header[11] == 0x50;

  static IffDeepFile IImageFileFormat<IffDeepFile>.FromFile(FileInfo file) => IffDeepReader.FromFile(file);
  static IffDeepFile IImageFileFormat<IffDeepFile>.FromBytes(byte[] data) => IffDeepReader.FromBytes(data);
  static IffDeepFile IImageFileFormat<IffDeepFile>.FromStream(Stream stream) => IffDeepReader.FromStream(stream);
  static byte[] IImageFileFormat<IffDeepFile>.ToBytes(IffDeepFile file) => IffDeepWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Whether the image has an alpha channel.</summary>
  public bool HasAlpha { get; init; }

  /// <summary>Compression method used for pixel data.</summary>
  public IffDeepCompression Compression { get; init; }

  /// <summary>Raw pixel data (RGB or RGBA, one byte per component).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this IFF DEEP file to a format-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IffDeepFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.HasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates an <see cref="IffDeepFile"/> from a format-independent <see cref="RawImage"/>.</summary>
  public static IffDeepFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return image.Format switch {
      PixelFormat.Rgb24 => new() {
        Width = image.Width,
        Height = image.Height,
        HasAlpha = false,
        Compression = IffDeepCompression.None,
        PixelData = image.PixelData[..],
      },
      PixelFormat.Rgba32 => new() {
        Width = image.Width,
        Height = image.Height,
        HasAlpha = true,
        Compression = IffDeepCompression.None,
        PixelData = image.PixelData[..],
      },
      _ => throw new ArgumentException($"Unsupported pixel format for IFF DEEP: {image.Format}", nameof(image)),
    };
  }
}
