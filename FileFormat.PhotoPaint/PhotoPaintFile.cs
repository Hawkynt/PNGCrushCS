using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PhotoPaint;

/// <summary>In-memory representation of a Corel Photo-Paint CPT image.</summary>
[FormatMagicBytes([0x43, 0x50, 0x54, 0x00])]
public sealed class PhotoPaintFile : IImageFileFormat<PhotoPaintFile> {

  /// <summary>"CPT\0" magic bytes identifying the format.</summary>
  internal static readonly byte[] Magic = [(byte)'C', (byte)'P', (byte)'T', 0x00];

  /// <summary>Size of the file header in bytes.</summary>
  internal const int HeaderSize = 24;

  /// <summary>Current format version.</summary>
  internal const ushort Version = 1;

  /// <summary>Bit depth for RGB24 pixel data.</summary>
  internal const ushort BitDepth = 24;

  static string IImageFileFormat<PhotoPaintFile>.PrimaryExtension => ".cpt";
  static string[] IImageFileFormat<PhotoPaintFile>.FileExtensions => [".cpt"];
  static PhotoPaintFile IImageFileFormat<PhotoPaintFile>.FromFile(FileInfo file) => PhotoPaintReader.FromFile(file);
  static PhotoPaintFile IImageFileFormat<PhotoPaintFile>.FromBytes(byte[] data) => PhotoPaintReader.FromBytes(data);
  static PhotoPaintFile IImageFileFormat<PhotoPaintFile>.FromStream(Stream stream) => PhotoPaintReader.FromStream(stream);
  static byte[] IImageFileFormat<PhotoPaintFile>.ToBytes(PhotoPaintFile file) => PhotoPaintWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts a PhotoPaint file to a <see cref="RawImage"/> with Rgb24 format.</summary>
  public static RawImage ToRawImage(PhotoPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates a PhotoPaint file from a <see cref="RawImage"/>. Must be Rgb24.</summary>
  public static PhotoPaintFile FromRawImage(RawImage image) {
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
