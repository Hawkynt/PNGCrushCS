using System;
using FileFormat.Core;

namespace FileFormat.CoCo;

/// <summary>In-memory representation of a TRS-80 CoCo PMODE 4 graphics screen (6144 bytes: 256x192 mono, 32 bytes/row).</summary>
public readonly record struct CoCoFile : IImageFormatReader<CoCoFile>, IImageToRawImage<CoCoFile>, IImageFromRawImage<CoCoFile>, IImageFormatWriter<CoCoFile> {

  static string IImageFormatMetadata<CoCoFile>.PrimaryExtension => ".coc";
  static string[] IImageFormatMetadata<CoCoFile>.FileExtensions => [".coc"];
  static CoCoFile IImageFormatReader<CoCoFile>.FromSpan(ReadOnlySpan<byte> data) => CoCoReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<CoCoFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<CoCoFile>.ToBytes(CoCoFile file) => CoCoWriter.ToBytes(file);

  /// <summary>Expected file size in bytes.</summary>
  internal const int ExpectedFileSize = 6144;

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 256;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline.</summary>
  internal const int BytesPerRow = 32;

  /// <summary>Always 256.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw pixel data (6144 bytes: 1bpp MSB-first, 32 bytes per row, 192 rows).</summary>
  public byte[] RawData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the CoCo screen to an Indexed1 raw image (256x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(CoCoFile file) {

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

  /// <summary>Creates a CoCo screen from an Indexed1 raw image (256x192).</summary>
  public static CoCoFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rawData = new byte[ExpectedFileSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, ExpectedFileSize)).CopyTo(rawData);

    return new CoCoFile { RawData = rawData };
  }
}
