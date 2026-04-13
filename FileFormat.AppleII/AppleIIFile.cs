using System;
using FileFormat.Core;

namespace FileFormat.AppleII;

/// <summary>In-memory representation of an Apple II Hi-Res Graphics image.</summary>
public sealed class AppleIIFile :
  IImageFormatReader<AppleIIFile>, IImageToRawImage<AppleIIFile>,
  IImageFromRawImage<AppleIIFile>, IImageFormatWriter<AppleIIFile> {

  static string IImageFormatMetadata<AppleIIFile>.PrimaryExtension => ".hgr";
  static string[] IImageFormatMetadata<AppleIIFile>.FileExtensions => [".hgr", ".dhgr"];
  static AppleIIFile IImageFormatReader<AppleIIFile>.FromSpan(ReadOnlySpan<byte> data) => AppleIIReader.FromSpan(data);
  static byte[] IImageFormatWriter<AppleIIFile>.ToBytes(AppleIIFile file) => AppleIIWriter.ToBytes(file);
  /// <summary>Width in pixels (280 for HGR, 560 for DHGR).</summary>
  public int Width { get; init; }
  /// <summary>Height in pixels (always 192).</summary>
  public int Height { get; init; }
  /// <summary>Graphics mode.</summary>
  public AppleIIMode Mode { get; init; }
  /// <summary>Pixel data in linear scanline order (deinterleaved).</summary>
  public byte[] PixelData { get; init; } = [];

  private const int _HEIGHT = 192;
  private const int _HGR_BYTES_PER_LINE = 40;
  private const int _DHGR_BYTES_PER_LINE = 80;
  private const int _PIXELS_PER_BYTE = 7;
  private const int _HGR_WIDTH = _HGR_BYTES_PER_LINE * _PIXELS_PER_BYTE;
  private const int _DHGR_WIDTH = _DHGR_BYTES_PER_LINE * _PIXELS_PER_BYTE;

  /// <summary>Converts an Apple II image to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(AppleIIFile file) {
    var bytesPerLine = file.Mode == AppleIIMode.Dhgr ? _DHGR_BYTES_PER_LINE : _HGR_BYTES_PER_LINE;
    var width = bytesPerLine * _PIXELS_PER_BYTE;
    var pixels = new byte[width * _HEIGHT];

    for (var y = 0; y < _HEIGHT; ++y) {
      var lineOffset = y * bytesPerLine;
      var pixelOffset = y * width;
      for (var byteIndex = 0; byteIndex < bytesPerLine; ++byteIndex) {
        var b = file.PixelData[lineOffset + byteIndex];
        for (var bit = 0; bit < _PIXELS_PER_BYTE; ++bit)
          pixels[pixelOffset + byteIndex * _PIXELS_PER_BYTE + bit] = (byte)((b >> bit) & 1);
      }
    }

    return new() {
      Width = width,
      Height = _HEIGHT,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates an <see cref="AppleIIFile"/> from a platform-independent <see cref="RawImage"/>.</summary>
  public static AppleIIFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("Only Indexed8 format is supported.", nameof(image));

    if (image.PaletteCount > 2)
      throw new ArgumentException("At most 2 palette entries are supported.", nameof(image));

    if (image.Height != _HEIGHT)
      throw new ArgumentException($"Height must be {_HEIGHT}.", nameof(image));

    AppleIIMode mode;
    int bytesPerLine;
    switch (image.Width) {
      case _HGR_WIDTH:
        mode = AppleIIMode.Hgr;
        bytesPerLine = _HGR_BYTES_PER_LINE;
        break;
      case _DHGR_WIDTH:
        mode = AppleIIMode.Dhgr;
        bytesPerLine = _DHGR_BYTES_PER_LINE;
        break;
      default:
        throw new ArgumentException($"Width must be {_HGR_WIDTH} (HGR) or {_DHGR_WIDTH} (DHGR).", nameof(image));
    }

    var pixelData = new byte[bytesPerLine * _HEIGHT];

    for (var y = 0; y < _HEIGHT; ++y) {
      var lineOffset = y * bytesPerLine;
      var pixelOffset = y * image.Width;
      for (var byteIndex = 0; byteIndex < bytesPerLine; ++byteIndex) {
        var b = 0;
        for (var bit = 0; bit < _PIXELS_PER_BYTE; ++bit) {
          if (image.PixelData[pixelOffset + byteIndex * _PIXELS_PER_BYTE + bit] != 0)
            b |= 1 << bit;
        }
        pixelData[lineOffset + byteIndex] = (byte)b;
      }
    }

    return new() {
      Width = image.Width,
      Height = _HEIGHT,
      Mode = mode,
      PixelData = pixelData,
    };
  }
}
