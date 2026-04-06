using System;
using FileFormat.Core;

namespace FileFormat.DrawIt;

/// <summary>In-memory representation of a DrawIt (DIT) image.</summary>
public readonly record struct DrawItFile : IImageFormatReader<DrawItFile>, IImageToRawImage<DrawItFile>, IImageFromRawImage<DrawItFile>, IImageFormatWriter<DrawItFile> {

  /// <summary>Size of the file header in bytes.</summary>
  public const int HeaderSize = DrawItHeader.StructSize;

  /// <summary>Number of palette entries (256 for 8-bit indexed).</summary>
  public const int PaletteEntries = 256;

  /// <summary>Size of the palette in bytes (256 entries * 3 bytes RGB).</summary>
  public const int PaletteDataSize = PaletteEntries * 3;

  static string IImageFormatMetadata<DrawItFile>.PrimaryExtension => ".dit";
  static string[] IImageFormatMetadata<DrawItFile>.FileExtensions => [".dit"];
  static DrawItFile IImageFormatReader<DrawItFile>.FromSpan(ReadOnlySpan<byte> data) => DrawItReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<DrawItFile>.Capabilities => FormatCapability.IndexedOnly | FormatCapability.VariableResolution;
  static byte[] IImageFormatWriter<DrawItFile>.ToBytes(DrawItFile file) => DrawItWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>256-entry RGB palette (768 bytes: R0,G0,B0,R1,G1,B1,...).</summary>
  public byte[] Palette { get; init; }

  /// <summary>8-bit indexed pixel data (width * height bytes).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(DrawItFile file) {

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = PaletteEntries,
    };
  }

  public static DrawItFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    if (image.Width is < 1 or > 65535)
      throw new ArgumentOutOfRangeException(nameof(image), "DrawIt width must be in the range 1..65535.");
    if (image.Height is < 1 or > 65535)
      throw new ArgumentOutOfRangeException(nameof(image), "DrawIt height must be in the range 1..65535.");

    var palette = new byte[PaletteDataSize];
    if (image.Palette is { } srcPalette)
      srcPalette.AsSpan(0, Math.Min(srcPalette.Length, PaletteDataSize)).CopyTo(palette);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Palette = palette,
      PixelData = image.PixelData[..],
    };
  }
}
