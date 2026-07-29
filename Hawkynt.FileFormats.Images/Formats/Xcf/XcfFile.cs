using System;
using FileFormat.Core;

namespace FileFormat.Xcf;

/// <summary>In-memory representation of an XCF image (flat composite of first layer).</summary>
[FormatMagicBytes([0x67, 0x69, 0x6D, 0x70, 0x20, 0x78, 0x63, 0x66])]
public readonly record struct XcfFile : IImageFormatReader<XcfFile>, IImageToRawImage<XcfFile>, IImageFromRawImage<XcfFile>, IImageFormatWriter<XcfFile> {

  static string IImageFormatMetadata<XcfFile>.PrimaryExtension => ".xcf";
  static string[] IImageFormatMetadata<XcfFile>.FileExtensions => [".xcf"];
  static XcfFile IImageFormatReader<XcfFile>.FromSpan(ReadOnlySpan<byte> data) => XcfReader.FromSpan(data);
  static byte[] IImageFormatWriter<XcfFile>.ToBytes(XcfFile file) => XcfWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public XcfColorMode ColorMode { get; init; }
  public int Version { get; init; }

  /// <summary>Flat pixel data: RGBA for RGB mode, GrayA for Grayscale, index bytes for Indexed.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Palette bytes (RGB triplets) for indexed mode, null otherwise.</summary>
  public byte[]? Palette { get; init; }

  public static RawImage ToRawImage(XcfFile file) {

    switch (file.ColorMode) {
      case XcfColorMode.Rgb:
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Rgba32,
          PixelData = file.PixelData[..],
        };
      case XcfColorMode.Grayscale:
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.GrayAlpha16,
          PixelData = file.PixelData[..],
        };
      case XcfColorMode.Indexed when file.Palette != null:
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Indexed8,
          PixelData = file.PixelData[..],
          Palette = file.Palette[..],
          PaletteCount = file.Palette.Length / 3,
        };
      default:
        throw new NotSupportedException($"XCF color mode {file.ColorMode} is not supported.");
    }
  }

  public static XcfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Rgba32:
        return new() {
          Width = image.Width,
          Height = image.Height,
          ColorMode = XcfColorMode.Rgb,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.GrayAlpha16:
        return new() {
          Width = image.Width,
          Height = image.Height,
          ColorMode = XcfColorMode.Grayscale,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Indexed8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          ColorMode = XcfColorMode.Indexed,
          PixelData = image.PixelData[..],
          Palette = image.Palette != null ? image.Palette[..] : null,
        };
      default:
        throw new ArgumentException($"Pixel format {image.Format} is not supported by XCF.", nameof(image));
    }
  }
}
