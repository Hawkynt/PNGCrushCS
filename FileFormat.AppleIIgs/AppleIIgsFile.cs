using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.AppleIIgs;

/// <summary>In-memory representation of an Apple IIGS Super Hi-Res ($C1) image (32768 bytes total).</summary>
public sealed class AppleIIgsFile :
  IImageFormatReader<AppleIIgsFile>, IImageToRawImage<AppleIIgsFile>,
  IImageFromRawImage<AppleIIgsFile>, IImageFormatWriter<AppleIIgsFile> {

  static string IImageFormatMetadata<AppleIIgsFile>.PrimaryExtension => ".shr";
  static string[] IImageFormatMetadata<AppleIIgsFile>.FileExtensions => [".shr", ".c1", ".pic"];
  static AppleIIgsFile IImageFormatReader<AppleIIgsFile>.FromSpan(ReadOnlySpan<byte> data) => AppleIIgsReader.FromSpan(data);
  static byte[] IImageFormatWriter<AppleIIgsFile>.ToBytes(AppleIIgsFile file) => AppleIIgsWriter.ToBytes(file);

  /// <summary>Image width in pixels (320 for Mode320, 640 for Mode640).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels, always 200.</summary>
  public int Height { get; init; }

  /// <summary>Display mode determined from SCB bit 7.</summary>
  public AppleIIgsMode Mode { get; init; }

  /// <summary>32000 bytes of 4bpp packed pixel data (200 lines x 160 bytes, 2 pixels per byte, high nibble first).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>200 bytes of scan control bytes (per-scanline: bits 0-3 = palette number, bit 5 = fill mode, bit 7 = 640 mode flag).</summary>
  public byte[] Scbs { get; init; } = [];

  /// <summary>256 palette entries as 16-bit LE values (16 palettes x 16 colors, each color is 0x0RGB with 4 bits per component).</summary>
  public short[] Palettes { get; init; } = [];

  private const int _HEIGHT = 200;
  private const int _BYTES_PER_LINE = 160;
  private const int _MODE320_WIDTH = 320;
  private const int _MODE640_WIDTH = 640;
  private const int _PALETTE_SIZE = 16;
  private const int _PIXELS_PER_BYTE_320 = 2;
  private const int _PIXELS_PER_BYTE_640 = 4;

  /// <summary>Converts an Apple IIGS image to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(AppleIIgsFile file) {
    var isMode640 = file.Mode == AppleIIgsMode.Mode640;
    var width = isMode640 ? _MODE640_WIDTH : _MODE320_WIDTH;
    var pixels = new byte[width * _HEIGHT * 3];

    for (var y = 0; y < _HEIGHT; ++y) {
      var paletteNum = file.Scbs[y] & 0x0F;
      var paletteBase = paletteNum * _PALETTE_SIZE;
      var lineOffset = y * _BYTES_PER_LINE;
      var pixelRowOffset = y * width * 3;

      if (isMode640) {
        for (var byteIndex = 0; byteIndex < _BYTES_PER_LINE; ++byteIndex) {
          var b = file.PixelData[lineOffset + byteIndex];
          var pixelBase = pixelRowOffset + byteIndex * _PIXELS_PER_BYTE_640 * 3;

          var idx0 = (b >> 6) & 0x03;
          var idx1 = (b >> 4) & 0x03;
          var idx2 = (b >> 2) & 0x03;
          var idx3 = b & 0x03;

          _WriteColor(pixels, pixelBase, file.Palettes[paletteBase + idx0]);
          _WriteColor(pixels, pixelBase + 3, file.Palettes[paletteBase + idx1]);
          _WriteColor(pixels, pixelBase + 6, file.Palettes[paletteBase + idx2]);
          _WriteColor(pixels, pixelBase + 9, file.Palettes[paletteBase + idx3]);
        }
      } else {
        for (var byteIndex = 0; byteIndex < _BYTES_PER_LINE; ++byteIndex) {
          var b = file.PixelData[lineOffset + byteIndex];
          var pixelBase = pixelRowOffset + byteIndex * _PIXELS_PER_BYTE_320 * 3;

          var hiNibble = (b >> 4) & 0x0F;
          var loNibble = b & 0x0F;

          _WriteColor(pixels, pixelBase, file.Palettes[paletteBase + hiNibble]);
          _WriteColor(pixels, pixelBase + 3, file.Palettes[paletteBase + loNibble]);
        }
      }
    }

    return new() {
      Width = width,
      Height = _HEIGHT,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static void _WriteColor(byte[] pixels, int offset, short color) {
    var r = (color >> 8) & 0x0F;
    var g = (color >> 4) & 0x0F;
    var b = color & 0x0F;
    pixels[offset] = (byte)((r << 4) | r);
    pixels[offset + 1] = (byte)((g << 4) | g);
    pixels[offset + 2] = (byte)((b << 4) | b);
  }

  /// <summary>Creates a Mode320 Apple IIGS SHR image from a <see cref="RawImage"/>. Uses a single 16-color palette for all scanlines. Input should be pre-quantized to ≤16 colors (Indexed8).</summary>
  public static AppleIIgsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var srcWidth = bgra.Width;
    var srcHeight = bgra.Height;
    var src = bgra.PixelData;

    // Build a 16-color palette from the most frequent colors
    var colorFreq = new Dictionary<int, int>();
    var totalPixels = srcWidth * srcHeight;
    for (var i = 0; i < totalPixels; ++i) {
      var si = i * 4;
      var r = (src[si + 2] >> 4) & 0x0F;
      var g = (src[si + 1] >> 4) & 0x0F;
      var b = (src[si] >> 4) & 0x0F;
      var key = (r << 8) | (g << 4) | b;
      colorFreq[key] = colorFreq.GetValueOrDefault(key) + 1;
    }

    // Take top 16 colors
    var sorted = new List<KeyValuePair<int, int>>(colorFreq);
    sorted.Sort((a, c) => c.Value.CompareTo(a.Value));
    var paletteEntries = new short[_PALETTE_SIZE * _PALETTE_SIZE]; // 16 palettes x 16 colors
    var paletteColorCount = Math.Min(sorted.Count, _PALETTE_SIZE);
    for (var i = 0; i < paletteColorCount; ++i)
      paletteEntries[i] = (short)sorted[i].Key;

    // Build reverse mapping
    var colorToIndex = new Dictionary<int, int>();
    for (var i = 0; i < paletteColorCount; ++i)
      colorToIndex[paletteEntries[i]] = i;

    // Generate 4bpp pixel data
    var pixelData = new byte[_HEIGHT * _BYTES_PER_LINE];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var byteIndex = 0; byteIndex < _BYTES_PER_LINE; ++byteIndex) {
        var x0 = byteIndex * _PIXELS_PER_BYTE_320;
        var x1 = x0 + 1;

        var idx0 = _FindClosest(src, srcWidth, srcHeight, x0, y, paletteEntries, paletteColorCount, colorToIndex);
        var idx1 = _FindClosest(src, srcWidth, srcHeight, x1, y, paletteEntries, paletteColorCount, colorToIndex);
        pixelData[y * _BYTES_PER_LINE + byteIndex] = (byte)((idx0 << 4) | idx1);
      }

    // All SCBs = 0 (palette 0, Mode320)
    var scbs = new byte[_HEIGHT];

    return new() {
      Width = _MODE320_WIDTH,
      Height = _HEIGHT,
      Mode = AppleIIgsMode.Mode320,
      PixelData = pixelData,
      Scbs = scbs,
      Palettes = paletteEntries,
    };
  }

  private static int _FindClosest(byte[] src, int srcW, int srcH, int x, int y, short[] palette, int count, Dictionary<int, int> lookup) {
    if (x >= srcW || y >= srcH)
      return 0;

    var si = (y * srcW + x) * 4;
    var r = (src[si + 2] >> 4) & 0x0F;
    var g = (src[si + 1] >> 4) & 0x0F;
    var b = (src[si] >> 4) & 0x0F;
    var key = (r << 8) | (g << 4) | b;

    if (lookup.TryGetValue(key, out var idx))
      return idx;

    // Nearest color search
    var bestDist = int.MaxValue;
    var bestIdx = 0;
    for (var i = 0; i < count; ++i) {
      var pr = (palette[i] >> 8) & 0x0F;
      var pg = (palette[i] >> 4) & 0x0F;
      var pb = palette[i] & 0x0F;
      var dr = r - pr;
      var dg = g - pg;
      var db = b - pb;
      var dist = dr * dr + dg * dg + db * db;
      if (dist < bestDist) {
        bestDist = dist;
        bestIdx = i;
      }
    }

    return bestIdx;
  }
}
