using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.WebShots;

/// <summary>In-memory representation of a WebShots image.</summary>
public sealed class WebShotsFile : IImageFileFormat<WebShotsFile> {

  static string IImageFileFormat<WebShotsFile>.PrimaryExtension => ".wb1";
  static string[] IImageFileFormat<WebShotsFile>.FileExtensions => [".wb1", ".wbc", ".wbp", ".wbz"];
  static WebShotsFile IImageFileFormat<WebShotsFile>.FromFile(FileInfo file) => WebShotsReader.FromFile(file);
  static WebShotsFile IImageFileFormat<WebShotsFile>.FromBytes(byte[] data) => WebShotsReader.FromBytes(data);
  static WebShotsFile IImageFileFormat<WebShotsFile>.FromStream(Stream stream) => WebShotsReader.FromStream(stream);
  static byte[] IImageFileFormat<WebShotsFile>.ToBytes(WebShotsFile file) => WebShotsWriter.ToBytes(file);

  /// <summary>Magic bytes: "WBST" (0x57 0x42 0x53 0x54).</summary>
  internal static readonly byte[] Magic = [0x57, 0x42, 0x53, 0x54];

  /// <summary>Header size: magic(4) + version(2) + width(2) + height(2) + bpp(2) + reserved(4) = 16 bytes.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>File version number.</summary>
  public ushort Version { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Raw pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this WebShots image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(WebShotsFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Not supported.</summary>
  public static WebShotsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to WebShotsFile is not supported.");
  }
}
