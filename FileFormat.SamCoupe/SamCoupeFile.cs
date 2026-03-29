using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SamCoupe;

/// <summary>In-memory representation of a SAM Coupe screen memory dump (24576 bytes).</summary>
public sealed class SamCoupeFile : IImageFileFormat<SamCoupeFile> {

  static string IImageFileFormat<SamCoupeFile>.PrimaryExtension => ".sam";
  static string[] IImageFileFormat<SamCoupeFile>.FileExtensions => [".sam"];
  static SamCoupeFile IImageFileFormat<SamCoupeFile>.FromFile(FileInfo file) => SamCoupeReader.FromFile(file);
  static SamCoupeFile IImageFileFormat<SamCoupeFile>.FromBytes(byte[] data) => SamCoupeReader.FromBytes(data);
  static SamCoupeFile IImageFileFormat<SamCoupeFile>.FromStream(Stream stream) => SamCoupeReader.FromStream(stream);
  static byte[] IImageFileFormat<SamCoupeFile>.ToBytes(SamCoupeFile file) => SamCoupeWriter.ToBytes(file);

  /// <summary>Width in pixels (256 for Mode 4, 512 for Mode 3).</summary>
  public int Width { get; init; }

  /// <summary>Always 192.</summary>
  public int Height { get; init; }

  /// <summary>Display mode.</summary>
  public SamCoupeMode Mode { get; init; }

  /// <summary>Linear pixel data in scanline order (deinterleaved from page layout).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this SAM Coupe screen to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(SamCoupeFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Mode switch {
      SamCoupeMode.Mode3 => _Mode3ToRawImage(file),
      SamCoupeMode.Mode4 => _Mode4ToRawImage(file),
      _ => throw new NotSupportedException($"Unsupported SAM Coupe mode: {file.Mode}.")
    };
  }

  /// <summary>Converts an Indexed8 <see cref="RawImage"/> back to a SAM Coupe screen.</summary>
  public static SamCoupeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    if (image.Height != 192)
      throw new ArgumentException("SAM Coupe images must be exactly 192 pixels tall.", nameof(image));

    var mode = image.PaletteCount <= 4 ? SamCoupeMode.Mode3 : SamCoupeMode.Mode4;

    return mode switch {
      SamCoupeMode.Mode3 => _Mode3FromRawImage(image),
      _ => _Mode4FromRawImage(image),
    };
  }

  private static RawImage _Mode3ToRawImage(SamCoupeFile file) {
    const int width = 512;
    const int height = 192;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
      for (var byteX = 0; byteX < 128; ++byteX) {
        var b = file.PixelData[y * 128 + byteX];
        var baseX = byteX * 4;
        pixels[y * width + baseX] = (byte)((b >> 6) & 0x03);
        pixels[y * width + baseX + 1] = (byte)((b >> 4) & 0x03);
        pixels[y * width + baseX + 2] = (byte)((b >> 2) & 0x03);
        pixels[y * width + baseX + 3] = (byte)(b & 0x03);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      PaletteCount = 4,
    };
  }

  private static RawImage _Mode4ToRawImage(SamCoupeFile file) {
    const int width = 256;
    const int height = 192;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
      for (var byteX = 0; byteX < 128; ++byteX) {
        var b = file.PixelData[y * 128 + byteX];
        var baseX = byteX * 2;
        pixels[y * width + baseX] = (byte)((b >> 4) & 0x0F);
        pixels[y * width + baseX + 1] = (byte)(b & 0x0F);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      PaletteCount = 16,
    };
  }

  private static SamCoupeFile _Mode3FromRawImage(RawImage image) {
    const int width = 512;
    const int height = 192;

    if (image.Width != width)
      throw new ArgumentException($"Mode 3 images must be exactly {width} pixels wide.", nameof(image));

    var packed = new byte[128 * height];

    for (var y = 0; y < height; ++y)
      for (var byteX = 0; byteX < 128; ++byteX) {
        var baseX = byteX * 4;
        var p0 = image.PixelData[y * width + baseX] & 0x03;
        var p1 = image.PixelData[y * width + baseX + 1] & 0x03;
        var p2 = image.PixelData[y * width + baseX + 2] & 0x03;
        var p3 = image.PixelData[y * width + baseX + 3] & 0x03;
        packed[y * 128 + byteX] = (byte)((p0 << 6) | (p1 << 4) | (p2 << 2) | p3);
      }

    return new() {
      Width = width,
      Height = height,
      Mode = SamCoupeMode.Mode3,
      PixelData = packed,
    };
  }

  private static SamCoupeFile _Mode4FromRawImage(RawImage image) {
    const int width = 256;
    const int height = 192;

    if (image.Width != width)
      throw new ArgumentException($"Mode 4 images must be exactly {width} pixels wide.", nameof(image));

    var packed = new byte[128 * height];

    for (var y = 0; y < height; ++y)
      for (var byteX = 0; byteX < 128; ++byteX) {
        var baseX = byteX * 2;
        var hi = image.PixelData[y * width + baseX] & 0x0F;
        var lo = image.PixelData[y * width + baseX + 1] & 0x0F;
        packed[y * 128 + byteX] = (byte)((hi << 4) | lo);
      }

    return new() {
      Width = width,
      Height = height,
      Mode = SamCoupeMode.Mode4,
      PixelData = packed,
    };
  }
}
