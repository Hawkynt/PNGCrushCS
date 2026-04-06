using System;
using FileFormat.Core;

namespace FileFormat.LogoSys;

/// <summary>In-memory representation of a Windows 95/98 boot logo (logo.sys) raw pixel dump.</summary>
public readonly record struct LogoSysFile : IImageFormatReader<LogoSysFile>, IImageToRawImage<LogoSysFile>, IImageFromRawImage<LogoSysFile>, IImageFormatWriter<LogoSysFile> {

  /// <summary>Pixel width of the logo image.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Pixel height of the logo image.</summary>
  internal const int PixelHeight = 400;

  /// <summary>Number of palette entries.</summary>
  internal const int PaletteEntries = 256;

  /// <summary>Bytes per palette entry (RGB).</summary>
  internal const int BytesPerPaletteEntry = 3;

  /// <summary>Size of the palette in bytes (256 * 3 = 768).</summary>
  internal const int PaletteSize = PaletteEntries * BytesPerPaletteEntry;

  /// <summary>Size of the pixel data in bytes (320 * 400 = 128000).</summary>
  internal const int PixelDataSize = PixelWidth * PixelHeight;

  /// <summary>Exact file size in bytes (768 palette + 128000 pixels = 128768).</summary>
  internal const int FileSize = PaletteSize + PixelDataSize;

  static string IImageFormatMetadata<LogoSysFile>.PrimaryExtension => ".sys";
  static string[] IImageFormatMetadata<LogoSysFile>.FileExtensions => [".sys", ".logo"];
  static LogoSysFile IImageFormatReader<LogoSysFile>.FromSpan(ReadOnlySpan<byte> data) => LogoSysReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<LogoSysFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<LogoSysFile>.ToBytes(LogoSysFile file) => LogoSysWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 400.</summary>
  public int Height => PixelHeight;

  /// <summary>256-color RGB palette (768 bytes: 256 entries, 3 bytes each in RGB order).</summary>
  public byte[] Palette { get; init; }

  /// <summary>8-bit indexed pixel data (128000 bytes: 320x400, top-to-bottom, left-to-right).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts the logo.sys file to an Indexed8 raw image (320x400 with 256-color palette).</summary>
  public static RawImage ToRawImage(LogoSysFile file) {

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = PaletteEntries,
    };
  }

  /// <summary>Creates a logo.sys file from an Indexed8 raw image (320x400).</summary>
  public static LogoSysFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));
    if (image.Palette == null || image.Palette.Length < PaletteSize)
      throw new ArgumentException($"Expected palette with at least {PaletteSize} bytes.", nameof(image));

    return new() {
      Palette = image.Palette[..PaletteSize],
      PixelData = image.PixelData[..PixelDataSize],
    };
  }
}
