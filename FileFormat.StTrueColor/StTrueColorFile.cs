using System;
using FileFormat.Core;

namespace FileFormat.StTrueColor;

/// <summary>In-memory representation of an ST True Color 12-bit image (Atari STE, 320x200, 4096 colors).</summary>
public readonly record struct StTrueColorFile : IImageFormatReader<StTrueColorFile>, IImageToRawImage<StTrueColorFile>, IImageFromRawImage<StTrueColorFile>, IImageFormatWriter<StTrueColorFile> {

  public const int FileSize = 128000;
  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;

  static string IImageFormatMetadata<StTrueColorFile>.PrimaryExtension => ".stc";
  static string[] IImageFormatMetadata<StTrueColorFile>.FileExtensions => [".stc"];
  static StTrueColorFile IImageFormatReader<StTrueColorFile>.FromSpan(ReadOnlySpan<byte> data) => StTrueColorReader.FromSpan(data);
  static byte[] IImageFormatWriter<StTrueColorFile>.ToBytes(StTrueColorFile file) => StTrueColorWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; }

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; }

  /// <summary>128000 bytes of 12-bit per pixel data (2 bytes per pixel, 320x200).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(StTrueColorFile file) {

    var rgb = new byte[_WIDTH * _HEIGHT * 3];
    var src = file.PixelData;

    for (var y = 0; y < _HEIGHT; ++y) {
      for (var x = 0; x < _WIDTH; ++x) {
        var srcOffset = (y * _WIDTH + x) * 2;
        var byte0 = src[srcOffset];
        var byte1 = src[srcOffset + 1];

        // 12-bit color: byte0 = RRRR GGGG, byte1 = BBBB 0000
        var r4 = (byte0 >> 4) & 0x0F;
        var g4 = byte0 & 0x0F;
        var b4 = (byte1 >> 4) & 0x0F;

        var dstOffset = (y * _WIDTH + x) * 3;
        rgb[dstOffset] = (byte)(r4 * 17);     // expand 4-bit to 8-bit (0x0->0, 0xF->255)
        rgb[dstOffset + 1] = (byte)(g4 * 17);
        rgb[dstOffset + 2] = (byte)(b4 * 17);
      }
    }

    return new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  public static StTrueColorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24.", nameof(image));
    if (image.Width != _WIDTH)
      throw new ArgumentException($"ST True Color images must be exactly {_WIDTH} pixels wide.", nameof(image));
    if (image.Height != _HEIGHT)
      throw new ArgumentException($"ST True Color images must be exactly {_HEIGHT} pixels tall.", nameof(image));

    var pixelData = new byte[FileSize];

    for (var y = 0; y < _HEIGHT; ++y) {
      for (var x = 0; x < _WIDTH; ++x) {
        var srcOffset = (y * _WIDTH + x) * 3;
        var r = image.PixelData[srcOffset];
        var g = image.PixelData[srcOffset + 1];
        var b = image.PixelData[srcOffset + 2];

        // Quantize 8-bit to 4-bit
        var r4 = r / 17;
        var g4 = g / 17;
        var b4 = b / 17;

        var dstOffset = (y * _WIDTH + x) * 2;
        pixelData[dstOffset] = (byte)((r4 << 4) | g4);
        pixelData[dstOffset + 1] = (byte)(b4 << 4);
      }
    }

    return new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      PixelData = pixelData,
    };
  }
}
