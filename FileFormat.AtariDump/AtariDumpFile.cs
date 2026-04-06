using System;
using FileFormat.Core;

namespace FileFormat.AtariDump;

/// <summary>In-memory representation of a generic Atari 8-bit screen dump. Default 320x192 Graphics 8 (1bpp monochrome).</summary>
public readonly record struct AtariDumpFile : IImageFormatReader<AtariDumpFile>, IImageToRawImage<AtariDumpFile>, IImageFromRawImage<AtariDumpFile>, IImageFormatWriter<AtariDumpFile> {

  /// <summary>Default image width in pixels (Graphics 8).</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default image height in pixels.</summary>
  internal const int DefaultHeight = 192;

  /// <summary>Default bytes per scanline (40 bytes = 320 pixels / 8 bits).</summary>
  internal const int DefaultBytesPerLine = DefaultWidth / 8;

  /// <summary>Default file size in bytes (40 bytes/line x 192 lines).</summary>
  internal const int DefaultFileSize = DefaultBytesPerLine * DefaultHeight;

  /// <summary>Minimum valid file size.</summary>
  internal const int MinFileSize = 40;

  /// <summary>Default ANTIC mode (0x0F = Graphics 8).</summary>
  internal const byte DefaultAnticMode = 0x0F;

  static string IImageFormatMetadata<AtariDumpFile>.PrimaryExtension => ".asd";
  static string[] IImageFormatMetadata<AtariDumpFile>.FileExtensions => [".asd", ".adm"];
  static AtariDumpFile IImageFormatReader<AtariDumpFile>.FromSpan(ReadOnlySpan<byte> data) => AtariDumpReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<AtariDumpFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<AtariDumpFile>.ToBytes(AtariDumpFile file) => AtariDumpWriter.ToBytes(file);

  /// <summary>Image width in pixels. Default 320.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels. Default 192.</summary>
  public int Height { get; init; }

  /// <summary>ANTIC display mode byte. Default 0x0F (Graphics 8).</summary>
  public byte AnticMode { get; init; }

  /// <summary>Raw screen dump data. For default Graphics 8: 1bpp MSB-first, 40 bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the screen dump to an Indexed1 raw image (default 320x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(AtariDumpFile file) {

    var bytesPerLine = file.Width / 8;
    var expectedSize = bytesPerLine * file.Height;
    var pixelData = new byte[expectedSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, expectedSize)).CopyTo(pixelData);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a screen dump from an Indexed1 raw image (320x192).</summary>
  public static AtariDumpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != DefaultWidth || image.Height != DefaultHeight)
      throw new ArgumentException($"Expected {DefaultWidth}x{DefaultHeight} but got {image.Width}x{image.Height}.", nameof(image));

    return new() {
      Width = DefaultWidth,
      Height = DefaultHeight,
      AnticMode = DefaultAnticMode,
      PixelData = image.PixelData[..],
    };
  }
}
