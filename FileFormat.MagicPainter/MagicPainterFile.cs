using System;
using FileFormat.Core;

namespace FileFormat.MagicPainter;

/// <summary>In-memory representation of a Magic Painter (MGP) image.
/// Header: 2-byte LE width, 2-byte LE height, 2-byte LE palette count, then palette (3 bytes per entry RGB), then raw indexed pixel data (1 byte per pixel).
/// </summary>
[FormatDetectionPriority(200)]
public readonly record struct MagicPainterFile : IImageFormatReader<MagicPainterFile>, IImageToRawImage<MagicPainterFile>, IImageFromRawImage<MagicPainterFile>, IImageFormatWriter<MagicPainterFile> {

  static string IImageFormatMetadata<MagicPainterFile>.PrimaryExtension => ".mgp";
  static string[] IImageFormatMetadata<MagicPainterFile>.FileExtensions => [".mgp"];
  static MagicPainterFile IImageFormatReader<MagicPainterFile>.FromSpan(ReadOnlySpan<byte> data) => MagicPainterReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<MagicPainterFile>.Capabilities => FormatCapability.VariableResolution | FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<MagicPainterFile>.ToBytes(MagicPainterFile file) => MagicPainterWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Palette entries as flat RGB triplets (3 bytes per entry).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Number of palette entries (1..256).</summary>
  public int PaletteCount { get; init; }

  /// <summary>Raw indexed pixel data (1 byte per pixel, index into palette).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this Magic Painter image to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(MagicPainterFile file) {

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = file.PaletteCount,
    };
  }

  /// <summary>Creates a Magic Painter image from a platform-independent <see cref="RawImage"/>.</summary>
  public static MagicPainterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    if (image.Width < 1 || image.Width > 65535)
      throw new ArgumentOutOfRangeException(nameof(image), "MGP width must be in the range 1..65535.");
    if (image.Height < 1 || image.Height > 65535)
      throw new ArgumentOutOfRangeException(nameof(image), "MGP height must be in the range 1..65535.");
    if (image.Palette == null || image.PaletteCount < 1)
      throw new ArgumentException("RawImage must have a palette.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Palette = image.Palette[..],
      PaletteCount = image.PaletteCount,
      PixelData = image.PixelData[..],
    };
  }
}
