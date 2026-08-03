using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PortfolioGraphics;

/// <summary>In-memory representation of an Atari Portfolio Graphics image (PGF/PGC).</summary>
public readonly record struct PortfolioGraphicsFile : IImageFormatReader<PortfolioGraphicsFile>, IImageToRawImage<PortfolioGraphicsFile>, IImageFromRawImage<PortfolioGraphicsFile>, IImageFormatWriter<PortfolioGraphicsFile> {

  /// <summary>Fixed pixel width.</summary>
  internal const int PixelWidth = 240;

  /// <summary>Fixed pixel height.</summary>
  internal const int PixelHeight = 64;

  /// <summary>Bytes per pixel row (240 / 8 = 30).</summary>
  internal const int BytesPerRow = PixelWidth / 8;

  /// <summary>Total pixel data size in bytes.</summary>
  internal const int PixelDataSize = BytesPerRow * PixelHeight;

  /// <summary>
  /// The one length a PGF has. Nothing precedes the bitmap: the screen is a fixed size, so there is
  /// nothing for a header to say.
  /// </summary>
  internal const int PgfFileSize = PixelDataSize;

  /// <summary>The three bytes the run-length form opens with, ahead of the packed screen.</summary>
  internal static ReadOnlySpan<byte> PgcSignature => [0x50, 0x47, 0x01];

  static string IImageFormatMetadata<PortfolioGraphicsFile>.PrimaryExtension => ".pgf";
  static string[] IImageFormatMetadata<PortfolioGraphicsFile>.FileExtensions => [".pgf", ".pgc"];
  static PortfolioGraphicsFile IImageFormatReader<PortfolioGraphicsFile>.FromSpan(ReadOnlySpan<byte> data) => PortfolioGraphicsReader.FromSpan(data);

  /// <summary>
  /// Reads a named file, the extension being what its reader needs.
  /// </summary>
  /// <remarks>
  /// The reader takes the extension into account and only the by-bytes entry was wired up here,
  /// so the registry could never reach it: whatever the extension would have settled was decided
  /// by a default instead. Ten formats carried this, each one otherwise found only when a sample
  /// happened to expose it.
  /// </remarks>
  static PortfolioGraphicsFile IImageFormatReader<PortfolioGraphicsFile>.FromFile(FileInfo file) => PortfolioGraphicsReader.FromFile(file);
  static VideoMode[] IImageFormatMetadata<PortfolioGraphicsFile>.VideoModes => [new("Default", [(240, 64)], [2])];
  static byte[] IImageFormatWriter<PortfolioGraphicsFile>.ToBytes(PortfolioGraphicsFile file) => PortfolioGraphicsWriter.ToBytes(file);

  /// <summary>Always 240.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 64.</summary>
  public int Height => PixelHeight;

  /// <summary>Packed 1bpp pixel data (1920 bytes).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Whether the bytes are run-length coded, which is what separates a PGC from a PGF.</summary>
  public bool Compressed { get; init; }

  /// <summary>
  /// Paper first, then ink. The Portfolio's screen is reflective, so a set bit is a dark pixel —
  /// building the palette the other way round shows every picture as its own negative.
  /// </summary>
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  /// <summary>Converts the Portfolio Graphics image to an Indexed1 raw image.</summary>
  public static RawImage ToRawImage(PortfolioGraphicsFile file) {

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
  public static PortfolioGraphicsFile FromRawImage(RawImage image) => FromRawImage(image, ".pgf");

  /// <summary>Creates the image in the shape the extension names: PGC is coded, PGF is not.</summary>
  /// <remarks>
  /// The two hold the same picture and differ only in whether it is run-length coded, so a PGC
  /// written as plain bytes came back decoded as though those bytes were codes.
  /// </remarks>
  public static PortfolioGraphicsFile FromRawImage(RawImage image, string extension) {
    ArgumentNullException.ThrowIfNull(image);

    var compressed = string.Equals(extension, ".pgc", StringComparison.OrdinalIgnoreCase);
    image = image.EnsureFormat(PixelFormat.Indexed1);
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[PixelDataSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, PixelDataSize)).CopyTo(pixelData);
    return new() { PixelData = pixelData, Compressed = compressed };
  }
}
