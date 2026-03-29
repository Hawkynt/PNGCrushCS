using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.CoCoMax;

/// <summary>In-memory representation of a CoCoMax paint program image (6144 bytes: 256x192 mono).</summary>
public sealed class CoCoMaxFile : IImageFileFormat<CoCoMaxFile> {

  static string IImageFileFormat<CoCoMaxFile>.PrimaryExtension => ".max";
  static string[] IImageFileFormat<CoCoMaxFile>.FileExtensions => [".max"];
  static FormatCapability IImageFileFormat<CoCoMaxFile>.Capabilities => FormatCapability.MonochromeOnly;
  static CoCoMaxFile IImageFileFormat<CoCoMaxFile>.FromFile(FileInfo file) => CoCoMaxReader.FromFile(file);
  static CoCoMaxFile IImageFileFormat<CoCoMaxFile>.FromBytes(byte[] data) => CoCoMaxReader.FromBytes(data);
  static CoCoMaxFile IImageFileFormat<CoCoMaxFile>.FromStream(Stream stream) => CoCoMaxReader.FromStream(stream);
  static RawImage IImageFileFormat<CoCoMaxFile>.ToRawImage(CoCoMaxFile file) => ToRawImage(file);
  static CoCoMaxFile IImageFileFormat<CoCoMaxFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<CoCoMaxFile>.ToBytes(CoCoMaxFile file) => CoCoMaxWriter.ToBytes(file);

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
  public byte[] RawData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the CoCoMax screen to an Indexed1 raw image (256x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(CoCoMaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

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

  /// <summary>Creates a CoCoMax screen from an Indexed1 raw image (256x192).</summary>
  public static CoCoMaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rawData = new byte[ExpectedFileSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, ExpectedFileSize)).CopyTo(rawData);

    return new CoCoMaxFile { RawData = rawData };
  }
}
