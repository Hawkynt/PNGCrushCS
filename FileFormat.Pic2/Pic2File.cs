using System;
using FileFormat.Core;

namespace FileFormat.Pic2;

/// <summary>In-memory representation of a PIC2 image.</summary>
public readonly record struct Pic2File : IImageFormatReader<Pic2File>, IImageToRawImage<Pic2File>, IImageFromRawImage<Pic2File>, IImageFormatWriter<Pic2File> {

  static string IImageFormatMetadata<Pic2File>.PrimaryExtension => ".p2";
  static string[] IImageFormatMetadata<Pic2File>.FileExtensions => [".p2"];
  static Pic2File IImageFormatReader<Pic2File>.FromSpan(ReadOnlySpan<byte> data) => Pic2Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Pic2File>.ToBytes(Pic2File file) => Pic2Writer.ToBytes(file);

  /// <summary>Magic bytes: "PIC2" (0x50 0x49 0x43 0x32).</summary>
  internal static readonly byte[] Magic = [0x50, 0x49, 0x43, 0x32];

  /// <summary>Header size in bytes.</summary>
  internal const int HeaderSize = 16;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per pixel.</summary>
  public ushort Bpp { get; init; }

  /// <summary>Image mode.</summary>
  public ushort Mode { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this PIC2 image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(Pic2File file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates a PIC2 file from a <see cref="RawImage"/>. Accepts Rgb24.</summary>
  public static Pic2File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Bpp = 24,
      PixelData = image.PixelData[..],
    };
  }
}
