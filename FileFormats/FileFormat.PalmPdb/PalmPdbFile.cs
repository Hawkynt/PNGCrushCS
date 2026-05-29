using System;
using FileFormat.Core;

namespace FileFormat.PalmPdb;

/// <summary>In-memory representation of a Palm PDB (Palm Database) image file.</summary>
[FormatMagicBytes([0x49, 0x6D, 0x67, 0x20], offset: 60)]
public readonly record struct PalmPdbFile : IImageFormatReader<PalmPdbFile>, IImageToRawImage<PalmPdbFile>, IImageFromRawImage<PalmPdbFile>, IImageFormatWriter<PalmPdbFile> {

  static string IImageFormatMetadata<PalmPdbFile>.PrimaryExtension => ".pdb";
  static string[] IImageFormatMetadata<PalmPdbFile>.FileExtensions => [".pdb"];
  static PalmPdbFile IImageFormatReader<PalmPdbFile>.FromSpan(ReadOnlySpan<byte> data) => PalmPdbReader.FromSpan(data);
  static byte[] IImageFormatWriter<PalmPdbFile>.ToBytes(PalmPdbFile file) => PalmPdbWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Database name (up to 31 characters, null-terminated in file).</summary>
  public string Name { get; init; }

  /// <summary>Raw RGB24 pixel data (3 bytes per pixel: R, G, B).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PalmPdbFile file) {
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
