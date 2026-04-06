using System;
using FileFormat.Core;

namespace FileFormat.Rgf;

/// <summary>In-memory representation of an RGF (LEGO Mindstorms EV3) image.</summary>
public readonly record struct RgfFile : IImageFormatReader<RgfFile>, IImageToRawImage<RgfFile>, IImageFromRawImage<RgfFile>, IImageFormatWriter<RgfFile> {

  static string IImageFormatMetadata<RgfFile>.PrimaryExtension => ".rgf";
  static string[] IImageFormatMetadata<RgfFile>.FileExtensions => [".rgf"];
  static RgfFile IImageFormatReader<RgfFile>.FromSpan(ReadOnlySpan<byte> data) => RgfReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<RgfFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<RgfFile>.ToBytes(RgfFile file) => RgfWriter.ToBytes(file);

  /// <summary>Image width in pixels (1-178).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (1-128).</summary>
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(RgfFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed1,
    PixelData = file.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static RgfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
