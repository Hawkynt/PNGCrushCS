using System;
using FileFormat.Core;

namespace FileFormat.WebShots;

/// <summary>In-memory representation of a WebShots image.</summary>
public readonly record struct WebShotsFile : IImageFormatReader<WebShotsFile>, IImageToRawImage<WebShotsFile>, IImageFormatWriter<WebShotsFile> {

  static string IImageFormatMetadata<WebShotsFile>.PrimaryExtension => ".wb1";
  static string[] IImageFormatMetadata<WebShotsFile>.FileExtensions => [".wb1", ".wbc", ".wbp", ".wbz"];
  static WebShotsFile IImageFormatReader<WebShotsFile>.FromSpan(ReadOnlySpan<byte> data) => WebShotsReader.FromSpan(data);
  static byte[] IImageFormatWriter<WebShotsFile>.ToBytes(WebShotsFile file) => WebShotsWriter.ToBytes(file);

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
  public byte[] PixelData { get; init; }

  /// <summary>Converts this WebShots image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(WebShotsFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

}
