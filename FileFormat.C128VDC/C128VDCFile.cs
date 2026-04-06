using System;
using FileFormat.Core;

namespace FileFormat.C128VDC;

/// <summary>In-memory representation of a C128 VDC 640x200 mono bitmap (16000 bytes: 80 bytes/row x 200 rows, 1bpp MSB-first).</summary>
public readonly record struct C128VDCFile : IImageFormatReader<C128VDCFile>, IImageToRawImage<C128VDCFile>, IImageFromRawImage<C128VDCFile>, IImageFormatWriter<C128VDCFile> {

  static string IImageFormatMetadata<C128VDCFile>.PrimaryExtension => ".vdc";
  static string[] IImageFormatMetadata<C128VDCFile>.FileExtensions => [".vdc", ".vdc3"];
  static C128VDCFile IImageFormatReader<C128VDCFile>.FromSpan(ReadOnlySpan<byte> data) => C128VDCReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<C128VDCFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<C128VDCFile>.ToBytes(C128VDCFile file) => C128VDCWriter.ToBytes(file);

  /// <summary>Expected file size in bytes (80 * 200).</summary>
  internal const int ExpectedFileSize = 16000;

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 640;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 200;

  /// <summary>Bytes per scanline.</summary>
  internal const int BytesPerRow = 80;

  /// <summary>Always 640.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw pixel data (16000 bytes: 1bpp MSB-first, 80 bytes per row, 200 rows).</summary>
  public byte[] RawData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the C128 VDC screen to an Indexed1 raw image (640x200, B&amp;W palette).</summary>
  public static RawImage ToRawImage(C128VDCFile file) {

    var pixelData = new byte[BytesPerRow * PixelHeight];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, pixelData.Length)).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a C128 VDC screen from an Indexed1 raw image (640x200).</summary>
  public static C128VDCFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rawData = new byte[ExpectedFileSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, ExpectedFileSize)).CopyTo(rawData);

    return new C128VDCFile { RawData = rawData };
  }
}
