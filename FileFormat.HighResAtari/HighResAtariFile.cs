using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HighResAtari;

/// <summary>In-memory representation of an Atari Hi-Res Paint image. 320x192 Graphics 8 monochrome.</summary>
public sealed class HighResAtariFile : IImageFileFormat<HighResAtariFile> {

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline (40 bytes = 320 pixels / 8 bits).</summary>
  internal const int BytesPerLine = PixelWidth / 8;

  /// <summary>Exact file size in bytes (40 bytes/line x 192 lines).</summary>
  internal const int FileSize = BytesPerLine * PixelHeight;

  static string IImageFileFormat<HighResAtariFile>.PrimaryExtension => ".hra";
  static string[] IImageFileFormat<HighResAtariFile>.FileExtensions => [".hra"];
  static FormatCapability IImageFileFormat<HighResAtariFile>.Capabilities => FormatCapability.MonochromeOnly;
  static HighResAtariFile IImageFileFormat<HighResAtariFile>.FromFile(FileInfo file) => HighResAtariReader.FromFile(file);
  static HighResAtariFile IImageFileFormat<HighResAtariFile>.FromBytes(byte[] data) => HighResAtariReader.FromBytes(data);
  static HighResAtariFile IImageFileFormat<HighResAtariFile>.FromStream(Stream stream) => HighResAtariReader.FromStream(stream);
  static RawImage IImageFileFormat<HighResAtariFile>.ToRawImage(HighResAtariFile file) => ToRawImage(file);
  static HighResAtariFile IImageFileFormat<HighResAtariFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<HighResAtariFile>.ToBytes(HighResAtariFile file) => HighResAtariWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw 1bpp screen data (7680 bytes: 40 bytes/line x 192 lines, MSB-first).</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the Hi-Res Paint image to an Indexed1 raw image (320x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(HighResAtariFile file) {
    ArgumentNullException.ThrowIfNull(file);

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
