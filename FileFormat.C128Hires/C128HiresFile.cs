using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.C128Hires;

/// <summary>In-memory representation of a C128 hires 320x200 mono bitmap (8000 bytes in 8x8 character cell order).</summary>
public sealed class C128HiresFile : IImageFileFormat<C128HiresFile> {

  static string IImageFileFormat<C128HiresFile>.PrimaryExtension => ".c1h";
  static string[] IImageFileFormat<C128HiresFile>.FileExtensions => [".c1h"];
  static FormatCapability IImageFileFormat<C128HiresFile>.Capabilities => FormatCapability.MonochromeOnly;
  static C128HiresFile IImageFileFormat<C128HiresFile>.FromFile(FileInfo file) => C128HiresReader.FromFile(file);
  static C128HiresFile IImageFileFormat<C128HiresFile>.FromBytes(byte[] data) => C128HiresReader.FromBytes(data);
  static C128HiresFile IImageFileFormat<C128HiresFile>.FromStream(Stream stream) => C128HiresReader.FromStream(stream);
  static RawImage IImageFileFormat<C128HiresFile>.ToRawImage(C128HiresFile file) => ToRawImage(file);
  static C128HiresFile IImageFileFormat<C128HiresFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<C128HiresFile>.ToBytes(C128HiresFile file) => C128HiresWriter.ToBytes(file);

  /// <summary>Expected file size in bytes (40 * 25 * 8).</summary>
  internal const int ExpectedFileSize = 8000;

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 320;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 200;

  /// <summary>Number of character cell columns (320 / 8).</summary>
  internal const int CellsX = 40;

  /// <summary>Number of character cell rows (200 / 8).</summary>
  internal const int CellsY = 25;

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw bitmap data (8000 bytes in 8x8 character cell order: cell[0] rows 0-7, cell[1] rows 0-7, ...).</summary>
  public byte[] RawData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the C128 hires screen to an Indexed1 raw image (320x200, B&amp;W palette).</summary>
  public static RawImage ToRawImage(C128HiresFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rowStride = PixelWidth / 8; // 40 bytes per row
    var pixelData = new byte[rowStride * PixelHeight];

    for (var cellY = 0; cellY < CellsY; ++cellY)
      for (var cellX = 0; cellX < CellsX; ++cellX) {
        var cellIndex = cellY * CellsX + cellX;
        for (var row = 0; row < 8; ++row) {
          var srcOffset = cellIndex * 8 + row;
          var b = srcOffset < file.RawData.Length ? file.RawData[srcOffset] : (byte)0;
          var dstY = cellY * 8 + row;
          var dstOffset = dstY * rowStride + cellX;
          pixelData[dstOffset] = b;
        }
      }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a C128 hires screen from an Indexed1 raw image (320x200).</summary>
  public static C128HiresFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rowStride = PixelWidth / 8;
    var rawData = new byte[ExpectedFileSize];

    for (var cellY = 0; cellY < CellsY; ++cellY)
      for (var cellX = 0; cellX < CellsX; ++cellX) {
        var cellIndex = cellY * CellsX + cellX;
        for (var row = 0; row < 8; ++row) {
          var srcY = cellY * 8 + row;
          var srcOffset = srcY * rowStride + cellX;
          var b = srcOffset < image.PixelData.Length ? image.PixelData[srcOffset] : (byte)0;
          rawData[cellIndex * 8 + row] = b;
        }
      }

    return new C128HiresFile { RawData = rawData };
  }
}
