using System;
using FileFormat.Core;

namespace FileFormat.Xyz;

/// <summary>In-memory representation of an RPG Maker 2000/2003 XYZ image.</summary>
[FormatMagicBytes([0x58, 0x59, 0x5A, 0x31])]
public readonly record struct XyzFile : IImageFormatReader<XyzFile>, IImageToRawImage<XyzFile>, IImageFromRawImage<XyzFile>, IImageFormatWriter<XyzFile> {

  static string IImageFormatMetadata<XyzFile>.PrimaryExtension => ".xyz";
  static string[] IImageFormatMetadata<XyzFile>.FileExtensions => [".xyz"];
  static XyzFile IImageFormatReader<XyzFile>.FromSpan(ReadOnlySpan<byte> data) => XyzReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<XyzFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<XyzFile>.ToBytes(XyzFile file) => XyzWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>256-entry RGB palette (768 bytes: R0,G0,B0, R1,G1,B1, ...).</summary>
  public byte[] Palette { get; init; }

  /// <summary>8-bit indexed pixel data (width * height bytes).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(XyzFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = 256,
    };
  }

  public static XyzFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));

    if (image.Palette == null || image.Palette.Length < 768)
      throw new ArgumentException("RawImage must have a 256-entry RGB palette (768 bytes).", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Palette = image.Palette[..],
      PixelData = image.PixelData[..],
    };
  }
}
