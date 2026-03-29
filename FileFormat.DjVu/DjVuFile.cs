using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DjVu;

/// <summary>In-memory representation of a single-page DjVu image.</summary>
[FormatMagicBytes([0x41, 0x54, 0x26, 0x54])]
public sealed class DjVuFile : IImageFileFormat<DjVuFile> {

  static string IImageFileFormat<DjVuFile>.PrimaryExtension => ".djvu";
  static string[] IImageFileFormat<DjVuFile>.FileExtensions => [".djvu", ".djv", ".iw4"];
  static DjVuFile IImageFileFormat<DjVuFile>.FromFile(FileInfo file) => DjVuReader.FromFile(file);
  static DjVuFile IImageFileFormat<DjVuFile>.FromBytes(byte[] data) => DjVuReader.FromBytes(data);
  static DjVuFile IImageFileFormat<DjVuFile>.FromStream(Stream stream) => DjVuReader.FromStream(stream);
  static RawImage IImageFileFormat<DjVuFile>.ToRawImage(DjVuFile file) => ToRawImage(file);
  static DjVuFile IImageFileFormat<DjVuFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<DjVuFile>.ToBytes(DjVuFile file) => DjVuWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>DjVu format version (major).</summary>
  public byte VersionMajor { get; init; }

  /// <summary>DjVu format version (minor).</summary>
  public byte VersionMinor { get; init; }

  /// <summary>Image resolution in dots per inch.</summary>
  public int Dpi { get; init; } = 300;

  /// <summary>Display gamma * 10 (e.g. 22 for gamma 2.2).</summary>
  public byte Gamma { get; init; } = 22;

  /// <summary>INFO flags byte (bit 0 = orientation).</summary>
  public byte Flags { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel, row-major, top-to-bottom).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Additional chunks preserved for round-trip fidelity (excluding INFO and PM44 pixel chunk).</summary>
  public IReadOnlyList<DjVuChunk> RawChunks { get; init; } = [];

  public static RawImage ToRawImage(DjVuFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static DjVuFile FromRawImage(RawImage image) {
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
