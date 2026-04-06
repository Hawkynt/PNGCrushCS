using System;
using FileFormat.Core;

namespace FileFormat.HighResAtari;

/// <summary>In-memory representation of an Atari Hi-Res Paint image. 320x192 Graphics 8 monochrome.</summary>
public readonly record struct HighResAtariFile : IImageFormatReader<HighResAtariFile>, IImageToRawImage<HighResAtariFile>, IImageFromRawImage<HighResAtariFile>, IImageFormatWriter<HighResAtariFile> {

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline (40 bytes = 320 pixels / 8 bits).</summary>
  internal const int BytesPerLine = PixelWidth / 8;

  /// <summary>Exact file size in bytes (40 bytes/line x 192 lines).</summary>
  internal const int FileSize = BytesPerLine * PixelHeight;

  static string IImageFormatMetadata<HighResAtariFile>.PrimaryExtension => ".hra";
  static string[] IImageFormatMetadata<HighResAtariFile>.FileExtensions => [".hra"];
  static HighResAtariFile IImageFormatReader<HighResAtariFile>.FromSpan(ReadOnlySpan<byte> data) => HighResAtariReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<HighResAtariFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<HighResAtariFile>.ToBytes(HighResAtariFile file) => HighResAtariWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw 1bpp screen data (7680 bytes: 40 bytes/line x 192 lines, MSB-first).</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the Hi-Res Paint image to an Indexed1 raw image (320x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(HighResAtariFile file) {

    var pixelData = new byte[FileSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, FileSize)).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a Hi-Res Paint image from an Indexed1 raw image (320x192).</summary>
  public static HighResAtariFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() { PixelData = image.PixelData[..] };
  }
}
