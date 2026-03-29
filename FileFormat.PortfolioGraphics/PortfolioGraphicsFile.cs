using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PortfolioGraphics;

/// <summary>In-memory representation of an Atari Portfolio Graphics image (PGF/PGC).</summary>
public sealed class PortfolioGraphicsFile : IImageFileFormat<PortfolioGraphicsFile> {

  /// <summary>Fixed pixel width.</summary>
  internal const int PixelWidth = 240;

  /// <summary>Fixed pixel height.</summary>
  internal const int PixelHeight = 64;

  /// <summary>Bytes per pixel row (240 / 8 = 30).</summary>
  internal const int BytesPerRow = PixelWidth / 8;

  /// <summary>Total pixel data size in bytes.</summary>
  internal const int PixelDataSize = BytesPerRow * PixelHeight;

  /// <summary>PGF header size.</summary>
  internal const int PgfHeaderSize = 8;

  /// <summary>PGF fixed file size.</summary>
  internal const int PgfFileSize = 3848;

  static string IImageFileFormat<PortfolioGraphicsFile>.PrimaryExtension => ".pgf";
  static string[] IImageFileFormat<PortfolioGraphicsFile>.FileExtensions => [".pgf", ".pgc"];
  static FormatCapability IImageFileFormat<PortfolioGraphicsFile>.Capabilities => FormatCapability.MonochromeOnly;
  static PortfolioGraphicsFile IImageFileFormat<PortfolioGraphicsFile>.FromFile(FileInfo file) => PortfolioGraphicsReader.FromFile(file);
  static PortfolioGraphicsFile IImageFileFormat<PortfolioGraphicsFile>.FromBytes(byte[] data) => PortfolioGraphicsReader.FromBytes(data);
  static PortfolioGraphicsFile IImageFileFormat<PortfolioGraphicsFile>.FromStream(Stream stream) => PortfolioGraphicsReader.FromStream(stream);
  static RawImage IImageFileFormat<PortfolioGraphicsFile>.ToRawImage(PortfolioGraphicsFile file) => ToRawImage(file);
  static PortfolioGraphicsFile IImageFileFormat<PortfolioGraphicsFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<PortfolioGraphicsFile>.ToBytes(PortfolioGraphicsFile file) => PortfolioGraphicsWriter.ToBytes(file);

  /// <summary>Always 240.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 64.</summary>
  public int Height => PixelHeight;

  /// <summary>Packed 1bpp pixel data (1920 bytes).</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the Portfolio Graphics image to an Indexed1 raw image.</summary>
  public static RawImage ToRawImage(PortfolioGraphicsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelData = new byte[PixelDataSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, PixelDataSize)).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a Portfolio Graphics image from an Indexed1 raw image (240x64).</summary>
  public static PortfolioGraphicsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[PixelDataSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, PixelDataSize)).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
