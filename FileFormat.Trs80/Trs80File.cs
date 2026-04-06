using System;
using FileFormat.Core;

namespace FileFormat.Trs80;

/// <summary>In-memory representation of a TRS-80 hi-res graphics screen dump (Model I/III).</summary>
public readonly record struct Trs80File : IImageFormatReader<Trs80File>, IImageToRawImage<Trs80File>, IImageFromRawImage<Trs80File>, IImageFormatWriter<Trs80File> {

  /// <summary>Number of character columns in the screen dump.</summary>
  internal const int Columns = 128;

  /// <summary>Number of character rows in the screen dump.</summary>
  internal const int Rows = 48;

  /// <summary>Exact file size in bytes (128 columns x 48 rows).</summary>
  internal const int FileSize = Columns * Rows;

  /// <summary>Effective pixel width (2 pixels per character cell horizontally).</summary>
  internal const int PixelWidth = Columns * 2;

  /// <summary>Effective pixel height (3 pixels per character cell vertically).</summary>
  internal const int PixelHeight = Rows * 3;

  static string IImageFormatMetadata<Trs80File>.PrimaryExtension => ".hr";
  static string[] IImageFormatMetadata<Trs80File>.FileExtensions => [".hr"];
  static Trs80File IImageFormatReader<Trs80File>.FromSpan(ReadOnlySpan<byte> data) => Trs80Reader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<Trs80File>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<Trs80File>.ToBytes(Trs80File file) => Trs80Writer.ToBytes(file);

  /// <summary>Always 256.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 144.</summary>
  public int Height => PixelHeight;

  /// <summary>Raw character cell data (6144 bytes: 128 columns x 48 rows). Each byte encodes a 2x3 pixel block.</summary>
  public byte[] RawData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>
  /// Converts the TRS-80 screen dump to an Indexed1 raw image (256x144, B&amp;W palette).
  /// Each character cell byte maps bits to a 2x3 pixel block:
  /// bit 0=top-left, bit 1=top-right, bit 2=mid-left, bit 3=mid-right, bit 4=bot-left, bit 5=bot-right.
  /// </summary>
  public static RawImage ToRawImage(Trs80File file) {

    var rowStride = PixelWidth / 8; // 32 bytes per row
    var pixelData = new byte[rowStride * PixelHeight];

    for (var cellRow = 0; cellRow < Rows; ++cellRow)
      for (var cellCol = 0; cellCol < Columns; ++cellCol) {
        var cellIndex = cellRow * Columns + cellCol;
        var b = cellIndex < file.RawData.Length ? file.RawData[cellIndex] : (byte)0;

        // Pixel x position for this cell (2 pixels wide)
        var px = cellCol * 2;
        // Pixel y position for this cell (3 pixels tall)
        var py = cellRow * 3;

        // bit 0 = top-left, bit 1 = top-right
        _SetPixel(pixelData, rowStride, px, py, (b & 0x01) != 0);
        _SetPixel(pixelData, rowStride, px + 1, py, (b & 0x02) != 0);

        // bit 2 = mid-left, bit 3 = mid-right
        _SetPixel(pixelData, rowStride, px, py + 1, (b & 0x04) != 0);
        _SetPixel(pixelData, rowStride, px + 1, py + 1, (b & 0x08) != 0);

        // bit 4 = bot-left, bit 5 = bot-right
        _SetPixel(pixelData, rowStride, px, py + 2, (b & 0x10) != 0);
        _SetPixel(pixelData, rowStride, px + 1, py + 2, (b & 0x20) != 0);
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

  /// <summary>Creates a TRS-80 screen dump from an Indexed1 raw image (256x144).</summary>
  public static Trs80File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rowStride = PixelWidth / 8; // 32 bytes per row
    var rawData = new byte[FileSize];

    for (var cellRow = 0; cellRow < Rows; ++cellRow)
      for (var cellCol = 0; cellCol < Columns; ++cellCol) {
        var px = cellCol * 2;
        var py = cellRow * 3;

        var b = 0;
        if (_GetPixel(image.PixelData, rowStride, px, py))
          b |= 0x01;
        if (_GetPixel(image.PixelData, rowStride, px + 1, py))
          b |= 0x02;
        if (_GetPixel(image.PixelData, rowStride, px, py + 1))
          b |= 0x04;
        if (_GetPixel(image.PixelData, rowStride, px + 1, py + 1))
          b |= 0x08;
        if (_GetPixel(image.PixelData, rowStride, px, py + 2))
          b |= 0x10;
        if (_GetPixel(image.PixelData, rowStride, px + 1, py + 2))
          b |= 0x20;

        rawData[cellRow * Columns + cellCol] = (byte)b;
      }

    return new() { RawData = rawData };
  }

  /// <summary>Sets a single pixel in 1bpp MSB-first packed data.</summary>
  private static void _SetPixel(byte[] data, int rowStride, int x, int y, bool set) {
    if (!set)
      return;

    var byteIndex = y * rowStride + x / 8;
    var bitIndex = 7 - (x % 8);
    data[byteIndex] |= (byte)(1 << bitIndex);
  }

  /// <summary>Gets a single pixel from 1bpp MSB-first packed data.</summary>
  private static bool _GetPixel(byte[] data, int rowStride, int x, int y) {
    var byteIndex = y * rowStride + x / 8;
    if (byteIndex >= data.Length)
      return false;

    var bitIndex = 7 - (x % 8);
    return (data[byteIndex] & (1 << bitIndex)) != 0;
  }
}
