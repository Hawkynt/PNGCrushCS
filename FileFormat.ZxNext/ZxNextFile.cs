using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxNext;

/// <summary>In-memory representation of a ZX Spectrum Next 256-color image (49152 bytes: 256x192 at 8bpp).</summary>
public sealed class ZxNextFile : IImageFileFormat<ZxNextFile> {

  static string IImageFileFormat<ZxNextFile>.PrimaryExtension => ".nxt";
  static string[] IImageFileFormat<ZxNextFile>.FileExtensions => [".nxt"];
  static FormatCapability IImageFileFormat<ZxNextFile>.Capabilities => FormatCapability.IndexedOnly;
  static ZxNextFile IImageFileFormat<ZxNextFile>.FromFile(FileInfo file) => ZxNextReader.FromFile(file);
  static ZxNextFile IImageFileFormat<ZxNextFile>.FromBytes(byte[] data) => ZxNextReader.FromBytes(data);
  static ZxNextFile IImageFileFormat<ZxNextFile>.FromStream(Stream stream) => ZxNextReader.FromStream(stream);
  static byte[] IImageFileFormat<ZxNextFile>.ToBytes(ZxNextFile file) => ZxNextWriter.ToBytes(file);

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>49152 bytes of 8bpp pixel data (1 byte per pixel, indexed into the 256-color palette).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>
  /// Default ZX Spectrum Next 256-color palette.
  /// 9-bit RGB (RRRGGGBB + 1 LSB for blue), stored as 768 bytes (256 entries x 3 bytes RGB).
  /// Standard Next palette: first 16 = classic ZX colors, remaining = 3-3-2 RGB.
  /// </summary>
  internal static byte[] BuildDefaultPalette() {
    var palette = new byte[256 * 3];

    // First 8: normal ZX Spectrum colors
    int[] normalColors = [0x000000, 0x0000CD, 0xCD0000, 0xCD00CD, 0x00CD00, 0x00CDCD, 0xCDCD00, 0xCDCDCD];
    // Next 8: bright ZX Spectrum colors
    int[] brightColors = [0x000000, 0x0000FF, 0xFF0000, 0xFF00FF, 0x00FF00, 0x00FFFF, 0xFFFF00, 0xFFFFFF];

    for (var i = 0; i < 8; ++i) {
      var idx = i * 3;
      palette[idx] = (byte)((normalColors[i] >> 16) & 0xFF);
      palette[idx + 1] = (byte)((normalColors[i] >> 8) & 0xFF);
      palette[idx + 2] = (byte)(normalColors[i] & 0xFF);
    }

    for (var i = 0; i < 8; ++i) {
      var idx = (i + 8) * 3;
      palette[idx] = (byte)((brightColors[i] >> 16) & 0xFF);
      palette[idx + 1] = (byte)((brightColors[i] >> 8) & 0xFF);
      palette[idx + 2] = (byte)(brightColors[i] & 0xFF);
    }

    // Remaining 240 entries: 3-3-2 RGB distribution
    for (var i = 16; i < 256; ++i) {
      var r3 = (i >> 5) & 0x07;
      var g3 = (i >> 2) & 0x07;
      var b2 = i & 0x03;
      var idx = i * 3;
      palette[idx] = (byte)(r3 * 255 / 7);
      palette[idx + 1] = (byte)(g3 * 255 / 7);
      palette[idx + 2] = (byte)(b2 * 255 / 3);
    }

    return palette;
  }

  /// <summary>Converts this Next image to Indexed8 with the default 256-color palette.</summary>
  public static RawImage ToRawImage(ZxNextFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = 256;
    const int height = 192;
    var palette = BuildDefaultPalette();
    var pixelData = new byte[width * height];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, pixelData.Length)).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixelData,
      Palette = palette,
      PaletteCount = 256,
    };
  }

  /// <summary>Creates a ZxNextFile from an Indexed8 RawImage.</summary>
  public static ZxNextFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new NotSupportedException($"ZxNextFile requires Indexed8 format, got {image.Format}.");
    if (image.Width != 256 || image.Height != 192)
      throw new NotSupportedException($"ZxNextFile requires 256x192 dimensions, got {image.Width}x{image.Height}.");

    var pixelData = new byte[256 * 192];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, pixelData.Length)).CopyTo(pixelData);

    return new ZxNextFile {
      PixelData = pixelData,
    };
  }
}
