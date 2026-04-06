using System;
using FileFormat.Core;

namespace FileFormat.PrintfoxPagefox;

/// <summary>In-memory representation of a Printfox/Pagefox hires image.</summary>
public readonly record struct PrintfoxPagefoxFile : IImageFormatReader<PrintfoxPagefoxFile>, IImageToRawImage<PrintfoxPagefoxFile>, IImageFormatWriter<PrintfoxPagefoxFile> {

  static string IImageFormatMetadata<PrintfoxPagefoxFile>.PrimaryExtension => ".bs";
  static string[] IImageFormatMetadata<PrintfoxPagefoxFile>.FileExtensions => [".bs", ".pg"];
  static PrintfoxPagefoxFile IImageFormatReader<PrintfoxPagefoxFile>.FromSpan(ReadOnlySpan<byte> data) => PrintfoxPagefoxReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PrintfoxPagefoxFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<PrintfoxPagefoxFile>.ToBytes(PrintfoxPagefoxFile file) => PrintfoxPagefoxWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Bytes per row (320 / 8 = 40).</summary>
  internal const int BytesPerRow = FixedWidth / 8;

  /// <summary>Minimum raw data size (40 bytes/row * 200 rows = 8000).</summary>
  internal const int MinDataSize = BytesPerRow * FixedHeight;

  /// <summary>Black and white palette (2 entries, 3 bytes each).</summary>
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw bitmap data (at least 8000 bytes of 1bpp packed pixel data).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this Printfox/Pagefox image to an Indexed1 <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(PrintfoxPagefoxFile file) {

    var pixelData = new byte[BytesPerRow * FixedHeight];
    var copyLength = Math.Min(file.RawData.Length, pixelData.Length);
    file.RawData.AsSpan(0, copyLength).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

}
