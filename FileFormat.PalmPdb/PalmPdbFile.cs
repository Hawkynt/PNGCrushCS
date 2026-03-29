using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PalmPdb;

/// <summary>In-memory representation of a Palm PDB (Palm Database) image file.</summary>
[FormatMagicBytes([0x49, 0x6D, 0x67, 0x20], offset: 60)]
public sealed class PalmPdbFile : IImageFileFormat<PalmPdbFile> {

  static string IImageFileFormat<PalmPdbFile>.PrimaryExtension => ".pdb";
  static string[] IImageFileFormat<PalmPdbFile>.FileExtensions => [".pdb"];
  static PalmPdbFile IImageFileFormat<PalmPdbFile>.FromFile(FileInfo file) => PalmPdbReader.FromFile(file);
  static PalmPdbFile IImageFileFormat<PalmPdbFile>.FromBytes(byte[] data) => PalmPdbReader.FromBytes(data);
  static PalmPdbFile IImageFileFormat<PalmPdbFile>.FromStream(Stream stream) => PalmPdbReader.FromStream(stream);
  static byte[] IImageFileFormat<PalmPdbFile>.ToBytes(PalmPdbFile file) => PalmPdbWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Database name (up to 31 characters, null-terminated in file).</summary>
  public string Name { get; init; } = "Image";

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel: R, G, B).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(PalmPdbFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static PalmPdbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    byte[] pixels;
    if (image.Format == PixelFormat.Rgb24)
      pixels = image.PixelData[..];
    else {
      var converted = PixelConverter.Convert(image, PixelFormat.Rgb24);
      pixels = converted.PixelData;
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixels,
    };
  }
}
