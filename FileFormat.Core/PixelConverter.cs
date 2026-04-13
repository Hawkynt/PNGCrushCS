using System;
using System.Runtime.Intrinsics;

namespace FileFormat.Core;

/// <summary>Converts pixel data between <see cref="PixelFormat"/> variants. Uses SIMD acceleration where available.</summary>
public static class PixelConverter {

  /// <summary>Converts a <see cref="RawImage"/> to a new <see cref="RawImage"/> with the specified pixel format.</summary>
  public static RawImage Convert(RawImage source, PixelFormat target) {
    ArgumentNullException.ThrowIfNull(source);

    if (source.Format == target)
      return source;

    var width = source.Width;
    var height = source.Height;
    var totalPixels = width * height;
    var data = source.PixelData;

    var converted = (source.Format, target) switch {
      (PixelFormat.Bgr24, PixelFormat.Bgra32) => BgrToBgra(data, totalPixels),
      (PixelFormat.Rgb24, PixelFormat.Bgra32) => RgbToBgra(data, totalPixels),
      (PixelFormat.Rgba32, PixelFormat.Bgra32) => RgbaToBgra(data, totalPixels),
      (PixelFormat.Gray8, PixelFormat.Bgra32) => Gray8ToBgra(data, totalPixels),
      (PixelFormat.Indexed8, PixelFormat.Bgra32) => IndexedToBgra(data, source.Palette ?? throw new InvalidOperationException("Palette required for indexed format"), totalPixels, source.AlphaTable),
      (PixelFormat.Indexed4, PixelFormat.Bgra32) => Indexed4ToBgra(data, source.Palette ?? throw new InvalidOperationException("Palette required for indexed format"), totalPixels, source.AlphaTable),
      (PixelFormat.Indexed1, PixelFormat.Bgra32) => Indexed1ToBgra(data, source.Palette ?? throw new InvalidOperationException("Palette required for indexed format"), totalPixels, source.AlphaTable),
      (PixelFormat.Rgba64, PixelFormat.Bgra32) => Rgba16BeToBgra(data, totalPixels),
      (PixelFormat.Rgb565, PixelFormat.Bgra32) => Rgb565ToBgra(data, totalPixels),
      (PixelFormat.Argb32, PixelFormat.Bgra32) => ArgbToBgra(data, totalPixels),
      (PixelFormat.Bgra32, PixelFormat.Rgb24) => BgraToRgb(data, totalPixels),
      (PixelFormat.Bgra32, PixelFormat.Rgba32) => BgraToRgba(data, totalPixels),
      (PixelFormat.Bgra32, PixelFormat.Bgr24) => BgraToBgr(data, totalPixels),
      (PixelFormat.Bgra32, PixelFormat.Gray8) => BgraToGray8(data, totalPixels),
      (PixelFormat.Bgra32, PixelFormat.Argb32) => BgraToArgb(data, totalPixels),
      (PixelFormat.Bgra32, PixelFormat.Rgb565) => BgraToRgb565(data, totalPixels),
      (PixelFormat.GrayAlpha16, PixelFormat.Bgra32) => GrayAlpha16ToBgra(data, totalPixels),
      (PixelFormat.Rgb48, PixelFormat.Bgra32) => Rgb48ToBgra(data, totalPixels),
      (PixelFormat.Rgba32, PixelFormat.Rgb24) => Rgba32ToRgb24(data, totalPixels),
      (PixelFormat.Rgb24, PixelFormat.Rgba32) => Rgb24ToRgba32(data, totalPixels),
      (PixelFormat.Gray8, PixelFormat.Rgb24) => Gray8ToRgb24(data, totalPixels),

      // Gray16 hub routes
      (PixelFormat.Gray16, PixelFormat.Bgra32) => Gray16ToBgra(data, totalPixels),
      (PixelFormat.Bgra32, PixelFormat.Gray16) => BgraToGray16(data, totalPixels),

      // 16-bit upscale from Bgra32
      (PixelFormat.Bgra32, PixelFormat.Rgb48) => BgraToRgb48(data, totalPixels),
      (PixelFormat.Bgra32, PixelFormat.Rgba64) => BgraToRgba64(data, totalPixels),

      // Direct 16↔16 routes (avoid lossy 8-bit hub)
      (PixelFormat.Rgb48, PixelFormat.Rgba64) => Rgb48ToRgba64(data, totalPixels),
      (PixelFormat.Rgba64, PixelFormat.Rgb48) => Rgba64ToRgb48(data, totalPixels),
      (PixelFormat.Gray16, PixelFormat.Rgb48) => Gray16ToRgb48(data, totalPixels),
      (PixelFormat.Gray16, PixelFormat.Rgba64) => Gray16ToRgba64(data, totalPixels),
      (PixelFormat.Rgb48, PixelFormat.Gray16) => Rgb48ToGray16(data, totalPixels),
      (PixelFormat.Rgba64, PixelFormat.Gray16) => Rgba64ToGray16(data, totalPixels),

      // Direct 16→8 shortcuts (avoid extra hub step)
      (PixelFormat.Rgb48, PixelFormat.Rgb24) => Rgb48ToRgb24(data, totalPixels),
      (PixelFormat.Rgba64, PixelFormat.Rgba32) => Rgba64ToRgba32(data, totalPixels),
      (PixelFormat.Gray16, PixelFormat.Gray8) => Gray16ToGray8(data, totalPixels),

      // Direct 8→16 upscale
      (PixelFormat.Rgb24, PixelFormat.Rgb48) => Rgb24ToRgb48(data, totalPixels),
      (PixelFormat.Rgba32, PixelFormat.Rgba64) => Rgba32ToRgba64(data, totalPixels),
      (PixelFormat.Gray8, PixelFormat.Gray16) => Gray8ToGray16(data, totalPixels),

      _ => _ConvertViaIntermediate(source, target),
    };

    return new() {
      Width = width,
      Height = height,
      Format = target,
      PixelData = converted,
    };
  }

  private static byte[] _ConvertViaIntermediate(RawImage source, PixelFormat target) {
    var bgra = Convert(source, PixelFormat.Bgra32);
    if (target == PixelFormat.Bgra32)
      return bgra.PixelData;

    return Convert(bgra, target).PixelData;
  }

  private static readonly Vector128<byte> _BgrExpandMask = Vector128.Create((byte)0, 1, 2, 0xFF, 3, 4, 5, 0xFF, 6, 7, 8, 0xFF, 9, 10, 11, 0xFF);
  private static readonly Vector128<byte> _RgbToBgraExpandMask = Vector128.Create((byte)2, 1, 0, 0xFF, 5, 4, 3, 0xFF, 8, 7, 6, 0xFF, 11, 10, 9, 0xFF);

  /// <summary>Converts BGR (3 bytes/pixel) to BGRA (4 bytes/pixel) with alpha 255.</summary>
  public static byte[] BgrToBgra(byte[] data, int totalPixels) => _Expand3To4(data, totalPixels, _BgrExpandMask, 0, 1, 2);

  /// <summary>Converts RGB (3 bytes/pixel) to BGRA (4 bytes/pixel) with alpha 255 and R/B swap.</summary>
  public static byte[] RgbToBgra(byte[] data, int totalPixels) => _Expand3To4(data, totalPixels, _RgbToBgraExpandMask, 2, 1, 0);

  private static readonly Vector128<byte> _RgbaBgraSwapMask = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
  private static readonly Vector128<byte> _ArgbBgraSwapMask = Vector128.Create((byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);

  /// <summary>Converts RGBA (4 bytes/pixel) to BGRA (4 bytes/pixel) by swapping R and B channels.</summary>
  public static byte[] RgbaToBgra(byte[] data, int totalPixels) => _Shuffle4To4(data, totalPixels, _RgbaBgraSwapMask, 2, 1, 0, 3);

  /// <summary>Converts 8-bit grayscale (1 byte/pixel) to BGRA (4 bytes/pixel).</summary>
  public static byte[] Gray8ToBgra(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 4];
    var i = 0;

    if (Vector128.IsHardwareAccelerated) {
      // Process 4 pixels: broadcast each gray byte to B,G,R positions, set A=255
      var broadcastMask = Vector128.Create((byte)0, 0, 0, 0xFF, 1, 1, 1, 0xFF, 2, 2, 2, 0xFF, 3, 3, 3, 0xFF);
      var alphaMask = Vector128.Create((byte)0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);
      var srcSpan = data.AsSpan();
      var dstSpan = result.AsSpan();

      for (; i + 16 <= totalPixels; i += 16) {
        // Process 16 pixels in 4 batches of 4
        var src = Vector128.Create(srcSpan.Slice(i, 16));

        var batch0 = Vector128.Shuffle(src, broadcastMask) | alphaMask;
        batch0.CopyTo(dstSpan.Slice(i * 4, 16));

        var mask1 = Vector128.Create((byte)4, 4, 4, 0xFF, 5, 5, 5, 0xFF, 6, 6, 6, 0xFF, 7, 7, 7, 0xFF);
        var batch1 = Vector128.Shuffle(src, mask1) | alphaMask;
        batch1.CopyTo(dstSpan.Slice(i * 4 + 16, 16));

        var mask2 = Vector128.Create((byte)8, 8, 8, 0xFF, 9, 9, 9, 0xFF, 10, 10, 10, 0xFF, 11, 11, 11, 0xFF);
        var batch2 = Vector128.Shuffle(src, mask2) | alphaMask;
        batch2.CopyTo(dstSpan.Slice(i * 4 + 32, 16));

        var mask3 = Vector128.Create((byte)12, 12, 12, 0xFF, 13, 13, 13, 0xFF, 14, 14, 14, 0xFF, 15, 15, 15, 0xFF);
        var batch3 = Vector128.Shuffle(src, mask3) | alphaMask;
        batch3.CopyTo(dstSpan.Slice(i * 4 + 48, 16));
      }
    }

    for (; i < totalPixels; ++i) {
      var gray = data[i];
      var dst = i * 4;
      result[dst] = gray;
      result[dst + 1] = gray;
      result[dst + 2] = gray;
      result[dst + 3] = 255;
    }

    return result;
  }

  /// <summary>Converts indexed 8-bit pixels with RGB palette (3 bytes/entry) to BGRA (4 bytes/pixel).</summary>
  public static byte[] IndexedToBgra(byte[] indices, byte[] palette, int totalPixels, byte[]? alphaTable = null) {
    var result = new byte[totalPixels * 4];
    var maxIndex = palette.Length / 3 - 1;

    for (var i = 0; i < totalPixels; ++i) {
      var idx = Math.Min(indices[i], maxIndex);
      var palOffset = idx * 3;
      var dst = i * 4;
      result[dst] = palette[palOffset + 2];
      result[dst + 1] = palette[palOffset + 1];
      result[dst + 2] = palette[palOffset];
      result[dst + 3] = alphaTable != null && idx < alphaTable.Length ? alphaTable[idx] : (byte)255;
    }

    return result;
  }

  /// <summary>Converts RGBA16 big-endian (8 bytes/pixel) to BGRA (4 bytes/pixel) by taking high bytes.</summary>
  public static byte[] Rgba16BeToBgra(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 4];
    var i = 0;

    if (Vector128.IsHardwareAccelerated) {
      // Process 4 pixels per iteration: 32 bytes input → 16 bytes output
      // Input per pixel: [Rh,Rl,Gh,Gl,Bh,Bl,Ah,Al] (big-endian 16-bit channels)
      // Output per pixel: [Bh,Gh,Rh,Ah] (BGRA with high bytes)
      var extractMask = Vector128.Create(
        (byte)4, 2, 0, 6,   // pixel 0: B_hi, G_hi, R_hi, A_hi
        12, 10, 8, 14,      // pixel 1
        0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF
      );
      var srcSpan = data.AsSpan();
      var dstSpan = result.AsSpan();

      for (; i + 4 <= totalPixels; i += 4) {
        // Process first 2 pixels
        var vec0 = Vector128.Create(srcSpan.Slice(i * 8, 16));
        var bgra0 = Vector128.Shuffle(vec0, extractMask);

        // Process next 2 pixels
        var vec1 = Vector128.Create(srcSpan.Slice(i * 8 + 16, 16));
        var bgra1 = Vector128.Shuffle(vec1, extractMask);

        // Combine: take low 8 bytes from each and merge into 16 bytes
        var combined = bgra0.WithUpper(bgra1.GetLower());
        combined.CopyTo(dstSpan.Slice(i * 4, 16));
      }
    }

    for (; i < totalPixels; ++i) {
      var src = i * 8;
      var dst = i * 4;
      result[dst] = data[src + 4];
      result[dst + 1] = data[src + 2];
      result[dst + 2] = data[src];
      result[dst + 3] = data[src + 6];
    }

    return result;
  }

  /// <summary>Converts RGB565 (2 bytes/pixel, little-endian) to BGRA (4 bytes/pixel).</summary>
  public static byte[] Rgb565ToBgra(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 4];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 2;
      var value = data[src] | (data[src + 1] << 8);
      var r = (value >> 11) & 0x1F;
      var g = (value >> 5) & 0x3F;
      var b = value & 0x1F;
      var dst = i * 4;
      result[dst] = (byte)(b * 255 / 31);
      result[dst + 1] = (byte)(g * 255 / 63);
      result[dst + 2] = (byte)(r * 255 / 31);
      result[dst + 3] = 255;
    }

    return result;
  }

  /// <summary>Converts ARGB (4 bytes/pixel) to BGRA (4 bytes/pixel).</summary>
  /// <summary>Converts ARGB (4 bytes/pixel) to BGRA (4 bytes/pixel) by reversing byte order per pixel.</summary>
  public static byte[] ArgbToBgra(byte[] data, int totalPixels) => _Shuffle4To4(data, totalPixels, _ArgbBgraSwapMask, 3, 2, 1, 0);

  private static readonly Vector128<byte> _BgraToRgbCompactMask = Vector128.Create((byte)2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, 0xFF, 0xFF, 0xFF, 0xFF);
  private static readonly Vector128<byte> _BgrCompactMask = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0xFF, 0xFF, 0xFF, 0xFF);
  private static readonly Vector128<byte> _RgbaToRgbCompactMask = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0xFF, 0xFF, 0xFF, 0xFF);

  /// <summary>Converts BGRA (4 bytes/pixel) to RGB (3 bytes/pixel), discarding alpha.</summary>
  public static byte[] BgraToRgb(byte[] data, int totalPixels) => _Compact4To3(data, totalPixels, _BgraToRgbCompactMask, 2, 1, 0);

  /// <summary>Converts BGRA (4 bytes/pixel) to RGBA (4 bytes/pixel) by swapping R and B.</summary>
  /// <summary>Converts BGRA (4 bytes/pixel) to RGBA (4 bytes/pixel) by swapping R and B channels.</summary>
  public static byte[] BgraToRgba(byte[] data, int totalPixels) => _Shuffle4To4(data, totalPixels, _RgbaBgraSwapMask, 2, 1, 0, 3);

  /// <summary>Converts BGRA (4 bytes/pixel) to BGR (3 bytes/pixel), discarding alpha.</summary>
  public static byte[] BgraToBgr(byte[] data, int totalPixels) => _Compact4To3(data, totalPixels, _BgrCompactMask, 0, 1, 2);

  /// <summary>Converts BGRA (4 bytes/pixel) to 8-bit grayscale using luminance formula: (R*77 + G*150 + B*29) >> 8.</summary>
  public static byte[] BgraToGray8(byte[] data, int totalPixels) {
    var result = new byte[totalPixels];
    var i = 0;

    if (Vector128.IsHardwareAccelerated) {
      // Shuffle masks to extract channels as ushort (channel byte, zero byte) for 4 pixels
      var rMask = Vector128.Create((byte)2, 0xFF, 6, 0xFF, 10, 0xFF, 14, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
      var gMask = Vector128.Create((byte)1, 0xFF, 5, 0xFF, 9, 0xFF, 13, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
      var bMask = Vector128.Create((byte)0, 0xFF, 4, 0xFF, 8, 0xFF, 12, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
      var w77 = Vector128.Create((ushort)77, 77, 77, 77, 0, 0, 0, 0);
      var w150 = Vector128.Create((ushort)150, 150, 150, 150, 0, 0, 0, 0);
      var w29 = Vector128.Create((ushort)29, 29, 29, 29, 0, 0, 0, 0);
      var srcSpan = data.AsSpan();

      for (; i + 8 <= totalPixels; i += 8) {
        // Process first 4 pixels
        var bgra0 = Vector128.Create(srcSpan.Slice(i * 4, 16));
        var r0 = Vector128.Shuffle(bgra0, rMask).AsUInt16();
        var g0 = Vector128.Shuffle(bgra0, gMask).AsUInt16();
        var b0 = Vector128.Shuffle(bgra0, bMask).AsUInt16();
        var gray0 = Vector128.ShiftRightLogical(r0 * w77 + g0 * w150 + b0 * w29, 8);

        // Process next 4 pixels
        var bgra1 = Vector128.Create(srcSpan.Slice(i * 4 + 16, 16));
        var r1 = Vector128.Shuffle(bgra1, rMask).AsUInt16();
        var g1 = Vector128.Shuffle(bgra1, gMask).AsUInt16();
        var b1 = Vector128.Shuffle(bgra1, bMask).AsUInt16();
        var gray1 = Vector128.ShiftRightLogical(r1 * w77 + g1 * w150 + b1 * w29, 8);

        // Narrow 8 ushort values to 8 bytes and write
        var grayBytes = Vector128.Narrow(gray0, gray1);
        // grayBytes layout: [g0,g1,g2,g3, 0,0,0,0, g4,g5,g6,g7, 0,0,0,0]
        result[i] = grayBytes.GetElement(0);
        result[i + 1] = grayBytes.GetElement(1);
        result[i + 2] = grayBytes.GetElement(2);
        result[i + 3] = grayBytes.GetElement(3);
        result[i + 4] = grayBytes.GetElement(8);
        result[i + 5] = grayBytes.GetElement(9);
        result[i + 6] = grayBytes.GetElement(10);
        result[i + 7] = grayBytes.GetElement(11);
      }
    }

    for (; i < totalPixels; ++i) {
      var src = i * 4;
      var b = data[src];
      var g = data[src + 1];
      var r = data[src + 2];
      result[i] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
    }

    return result;
  }

  /// <summary>Converts BGRA (4 bytes/pixel) to ARGB (4 bytes/pixel).</summary>
  /// <summary>Converts BGRA (4 bytes/pixel) to ARGB (4 bytes/pixel) by reversing byte order per pixel.</summary>
  public static byte[] BgraToArgb(byte[] data, int totalPixels) => _Shuffle4To4(data, totalPixels, _ArgbBgraSwapMask, 3, 2, 1, 0);

  /// <summary>Converts BGRA (4 bytes/pixel) to RGB565 (2 bytes/pixel, little-endian).</summary>
  public static byte[] BgraToRgb565(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 2];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 4;
      var b = data[src];
      var g = data[src + 1];
      var r = data[src + 2];
      var value = ((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3);
      var dst = i * 2;
      result[dst] = (byte)value;
      result[dst + 1] = (byte)(value >> 8);
    }

    return result;
  }

  /// <summary>Converts GrayAlpha16 (2 bytes/pixel: gray+alpha) to BGRA (4 bytes/pixel).</summary>
  public static byte[] GrayAlpha16ToBgra(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 4];
    var i = 0;

    if (Vector128.IsHardwareAccelerated) {
      // Process 8 pixels at a time: 16 src bytes (GA GA GA GA GA GA GA GA) → 32 dst bytes (BGRA x8)
      var shuffleGray = Vector128.Create((byte)0, 0, 0, 1, 2, 2, 2, 3, 4, 4, 4, 5, 6, 6, 6, 7);
      var shuffleAlpha = Vector128.Create((byte)8, 8, 8, 9, 10, 10, 10, 11, 12, 12, 12, 13, 14, 14, 14, 15);
      for (; i <= totalPixels - 8; i += 8) {
        var src = Vector128.Create(data, i * 2);
        var lo = Vector128.Shuffle(src, shuffleGray);
        var hi = Vector128.Shuffle(src, shuffleAlpha);
        // Interleave gray broadcast with alpha: B=G=R=gray, A=alpha
        var alphaMask = Vector128.Create((byte)0, 0, 0, 0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF);
        var grayMask = Vector128.Create((byte)0xFF, 0xFF, 0xFF, 0, 0xFF, 0xFF, 0xFF, 0, 0xFF, 0xFF, 0xFF, 0, 0xFF, 0xFF, 0xFF, 0);
        // For first 4 pixels: gray from lo, alpha from original positions
        var pix03 = Vector128.Create(
          data[i * 2], data[i * 2], data[i * 2], data[i * 2 + 1],
          data[i * 2 + 2], data[i * 2 + 2], data[i * 2 + 2], data[i * 2 + 3],
          data[i * 2 + 4], data[i * 2 + 4], data[i * 2 + 4], data[i * 2 + 5],
          data[i * 2 + 6], data[i * 2 + 6], data[i * 2 + 6], data[i * 2 + 7]);
        pix03.CopyTo(result, i * 4);
        var pix47 = Vector128.Create(
          data[i * 2 + 8], data[i * 2 + 8], data[i * 2 + 8], data[i * 2 + 9],
          data[i * 2 + 10], data[i * 2 + 10], data[i * 2 + 10], data[i * 2 + 11],
          data[i * 2 + 12], data[i * 2 + 12], data[i * 2 + 12], data[i * 2 + 13],
          data[i * 2 + 14], data[i * 2 + 14], data[i * 2 + 14], data[i * 2 + 15]);
        pix47.CopyTo(result, i * 4 + 16);
      }
    }

    for (; i < totalPixels; ++i) {
      var src = i * 2;
      var gray = data[src];
      var alpha = data[src + 1];
      var dst = i * 4;
      result[dst] = gray;
      result[dst + 1] = gray;
      result[dst + 2] = gray;
      result[dst + 3] = alpha;
    }

    return result;
  }

  /// <summary>Converts indexed 4-bit pixels (2 pixels per byte, high nibble first) with RGB palette to BGRA (4 bytes/pixel).</summary>
  public static byte[] Indexed4ToBgra(byte[] data, byte[] palette, int totalPixels, byte[]? alphaTable = null) {
    var result = new byte[totalPixels * 4];
    var maxIndex = palette.Length / 3 - 1;

    for (var i = 0; i < totalPixels; ++i) {
      var byteIndex = i >> 1;
      var idx = (i & 1) == 0 ? (data[byteIndex] >> 4) & 0x0F : data[byteIndex] & 0x0F;
      idx = Math.Min(idx, maxIndex);
      var palOffset = idx * 3;
      var dst = i * 4;
      result[dst] = palette[palOffset + 2];
      result[dst + 1] = palette[palOffset + 1];
      result[dst + 2] = palette[palOffset];
      result[dst + 3] = alphaTable != null && idx < alphaTable.Length ? alphaTable[idx] : (byte)255;
    }

    return result;
  }

  /// <summary>Converts indexed 1-bit pixels (8 pixels per byte, MSB first) with RGB palette to BGRA (4 bytes/pixel).</summary>
  public static byte[] Indexed1ToBgra(byte[] data, byte[] palette, int totalPixels, byte[]? alphaTable = null) {
    var result = new byte[totalPixels * 4];
    var maxIndex = palette.Length / 3 - 1;

    for (var i = 0; i < totalPixels; ++i) {
      var byteIndex = i >> 3;
      var bitIndex = 7 - (i & 7);
      var idx = (data[byteIndex] >> bitIndex) & 1;
      idx = Math.Min(idx, maxIndex);
      var palOffset = idx * 3;
      var dst = i * 4;
      result[dst] = palette[palOffset + 2];
      result[dst + 1] = palette[palOffset + 1];
      result[dst + 2] = palette[palOffset];
      result[dst + 3] = alphaTable != null && idx < alphaTable.Length ? alphaTable[idx] : (byte)255;
    }

    return result;
  }

  /// <summary>Converts RGB48 (6 bytes/pixel, big-endian 16-bit channels) to BGRA (4 bytes/pixel).</summary>
  public static byte[] Rgb48ToBgra(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 4];
    var i = 0;

    if (Vector128.IsHardwareAccelerated) {
      // Process 2 pixels per iteration: 12 bytes input → 8 bytes output
      // Input: [Rh0,Rl0,Gh0,Gl0,Bh0,Bl0, Rh1,Rl1,Gh1,Gl1,Bh1,Bl1, ...]
      // Output: [Bh0,Gh0,Rh0,255, Bh1,Gh1,Rh1,255, ...]
      var extractMask = Vector128.Create(
        (byte)4, 2, 0, 0xFF,  // pixel 0: B_hi, G_hi, R_hi, 0xFF→0
        10, 8, 6, 0xFF,       // pixel 1
        0xFF, 0xFF, 0xFF, 0xFF,
        0xFF, 0xFF, 0xFF, 0xFF
      );
      var alphaMask = Vector128.Create((byte)0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0);
      var srcSpan = data.AsSpan();
      var dstSpan = result.AsSpan();

      for (; i + 2 <= totalPixels && (i + 2) * 6 <= data.Length; i += 2) {
        var vec = Vector128.Create(srcSpan.Slice(i * 6, 16));
        var bgra = Vector128.Shuffle(vec, extractMask) | alphaMask;
        // Write 8 bytes (2 BGRA pixels)
        dstSpan[i * 4] = bgra.GetElement(0);
        dstSpan[i * 4 + 1] = bgra.GetElement(1);
        dstSpan[i * 4 + 2] = bgra.GetElement(2);
        dstSpan[i * 4 + 3] = bgra.GetElement(3);
        dstSpan[i * 4 + 4] = bgra.GetElement(4);
        dstSpan[i * 4 + 5] = bgra.GetElement(5);
        dstSpan[i * 4 + 6] = bgra.GetElement(6);
        dstSpan[i * 4 + 7] = bgra.GetElement(7);
      }
    }

    for (; i < totalPixels; ++i) {
      var src = i * 6;
      var dst = i * 4;
      result[dst] = data[src + 4];
      result[dst + 1] = data[src + 2];
      result[dst + 2] = data[src];
      result[dst + 3] = 255;
    }

    return result;
  }

  /// <summary>Converts RGBA (4 bytes/pixel) to RGB (3 bytes/pixel), discarding alpha.</summary>
  public static byte[] Rgba32ToRgb24(byte[] data, int totalPixels) => _Compact4To3(data, totalPixels, _RgbaToRgbCompactMask, 0, 1, 2);

  /// <summary>Converts RGB (3 bytes/pixel) to RGBA (4 bytes/pixel) with alpha 255.</summary>
  public static byte[] Rgb24ToRgba32(byte[] data, int totalPixels) => _Expand3To4(data, totalPixels, _BgrExpandMask, 0, 1, 2);

  /// <summary>Converts 8-bit grayscale (1 byte/pixel) to RGB (3 bytes/pixel), replicating the gray value to R, G, B.</summary>
  public static byte[] Gray8ToRgb24(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 3];
    var i = 0;

    if (Vector128.IsHardwareAccelerated) {
      // Process 5 pixels per iteration: 5 gray bytes → 15 RGB bytes
      var replicateMask = Vector128.Create((byte)0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 0xFF);
      var srcSpan = data.AsSpan();
      var dstSpan = result.AsSpan();

      for (; i + 16 <= totalPixels; i += 16) {
        var src = Vector128.Create(srcSpan.Slice(i, 16));

        // Batch 0: pixels 0-4 (15 useful bytes)
        var batch0 = Vector128.Shuffle(src, replicateMask);
        batch0.GetLower().CopyTo(dstSpan.Slice(i * 3, 8));
        dstSpan[i * 3 + 8] = batch0.GetElement(8);
        dstSpan[i * 3 + 9] = batch0.GetElement(9);
        dstSpan[i * 3 + 10] = batch0.GetElement(10);
        dstSpan[i * 3 + 11] = batch0.GetElement(11);
        dstSpan[i * 3 + 12] = batch0.GetElement(12);
        dstSpan[i * 3 + 13] = batch0.GetElement(13);
        dstSpan[i * 3 + 14] = batch0.GetElement(14);

        // Batch 1: pixels 5-9
        var mask1 = Vector128.Create((byte)5, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9, 9, 0xFF);
        var batch1 = Vector128.Shuffle(src, mask1);
        batch1.GetLower().CopyTo(dstSpan.Slice(i * 3 + 15, 8));
        dstSpan[i * 3 + 23] = batch1.GetElement(8);
        dstSpan[i * 3 + 24] = batch1.GetElement(9);
        dstSpan[i * 3 + 25] = batch1.GetElement(10);
        dstSpan[i * 3 + 26] = batch1.GetElement(11);
        dstSpan[i * 3 + 27] = batch1.GetElement(12);
        dstSpan[i * 3 + 28] = batch1.GetElement(13);
        dstSpan[i * 3 + 29] = batch1.GetElement(14);

        // Batch 2: pixels 10-14
        var mask2 = Vector128.Create((byte)10, 10, 10, 11, 11, 11, 12, 12, 12, 13, 13, 13, 14, 14, 14, 0xFF);
        var batch2 = Vector128.Shuffle(src, mask2);
        batch2.GetLower().CopyTo(dstSpan.Slice(i * 3 + 30, 8));
        dstSpan[i * 3 + 38] = batch2.GetElement(8);
        dstSpan[i * 3 + 39] = batch2.GetElement(9);
        dstSpan[i * 3 + 40] = batch2.GetElement(10);
        dstSpan[i * 3 + 41] = batch2.GetElement(11);
        dstSpan[i * 3 + 42] = batch2.GetElement(12);
        dstSpan[i * 3 + 43] = batch2.GetElement(13);
        dstSpan[i * 3 + 44] = batch2.GetElement(14);

        // Pixel 15: scalar (only 1 pixel left, 3 bytes)
        var gray15 = src.GetElement(15);
        dstSpan[i * 3 + 45] = gray15;
        dstSpan[i * 3 + 46] = gray15;
        dstSpan[i * 3 + 47] = gray15;
      }
    }

    for (; i < totalPixels; ++i) {
      var gray = data[i];
      var dst = i * 3;
      result[dst] = gray;
      result[dst + 1] = gray;
      result[dst + 2] = gray;
    }

    return result;
  }

  // ── 16-bit Gray16 hub routes ────────────────────────────────────────────────

  /// <summary>Converts 16-bit big-endian grayscale (2 bytes/pixel) to BGRA (4 bytes/pixel).</summary>
  public static byte[] Gray16ToBgra(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 4];

    for (var i = 0; i < totalPixels; ++i) {
      var gray = data[i * 2]; // high byte
      var dst = i * 4;
      result[dst] = gray;
      result[dst + 1] = gray;
      result[dst + 2] = gray;
      result[dst + 3] = 255;
    }

    return result;
  }

  /// <summary>Converts BGRA (4 bytes/pixel) to 16-bit big-endian grayscale (2 bytes/pixel) using luminance. Upscales via v*257.</summary>
  public static byte[] BgraToGray16(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 2];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 4;
      var b = data[src];
      var g = data[src + 1];
      var r = data[src + 2];
      var gray8 = (byte)((r * 77 + g * 150 + b * 29) >> 8);
      var dst = i * 2;
      result[dst] = gray8;     // high byte (v*257 >> 8 == v)
      result[dst + 1] = gray8; // low byte  (v*257 & 0xFF == v)
    }

    return result;
  }

  // ── 16-bit upscale from Bgra32 ────────────────────────────────────────────

  /// <summary>Converts BGRA (4 bytes/pixel) to RGB48 big-endian (6 bytes/pixel). Upscales 8→16 bit via v*257.</summary>
  public static byte[] BgraToRgb48(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 6];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 4;
      var b = data[src];
      var g = data[src + 1];
      var r = data[src + 2];
      var dst = i * 6;
      // RGB big-endian 16-bit: v*257 = (v<<8)|v, so hi=v, lo=v
      result[dst] = r;
      result[dst + 1] = r;
      result[dst + 2] = g;
      result[dst + 3] = g;
      result[dst + 4] = b;
      result[dst + 5] = b;
    }

    return result;
  }

  /// <summary>Converts BGRA (4 bytes/pixel) to RGBA64 big-endian (8 bytes/pixel). Upscales 8→16 bit via v*257.</summary>
  public static byte[] BgraToRgba64(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 8];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 4;
      var b = data[src];
      var g = data[src + 1];
      var r = data[src + 2];
      var a = data[src + 3];
      var dst = i * 8;
      result[dst] = r;
      result[dst + 1] = r;
      result[dst + 2] = g;
      result[dst + 3] = g;
      result[dst + 4] = b;
      result[dst + 5] = b;
      result[dst + 6] = a;
      result[dst + 7] = a;
    }

    return result;
  }

  // ── Direct 16↔16 routes ───────────────────────────────────────────────────

  /// <summary>Converts RGB48 big-endian (6 bytes/pixel) to RGBA64 big-endian (8 bytes/pixel) with opaque alpha.</summary>
  public static byte[] Rgb48ToRgba64(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 8];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 6;
      var dst = i * 8;
      result[dst] = data[src];         // R hi
      result[dst + 1] = data[src + 1]; // R lo
      result[dst + 2] = data[src + 2]; // G hi
      result[dst + 3] = data[src + 3]; // G lo
      result[dst + 4] = data[src + 4]; // B hi
      result[dst + 5] = data[src + 5]; // B lo
      result[dst + 6] = 0xFF;          // A hi
      result[dst + 7] = 0xFF;          // A lo
    }

    return result;
  }

  /// <summary>Converts RGBA64 big-endian (8 bytes/pixel) to RGB48 big-endian (6 bytes/pixel), discarding alpha.</summary>
  public static byte[] Rgba64ToRgb48(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 6];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 8;
      var dst = i * 6;
      result[dst] = data[src];         // R hi
      result[dst + 1] = data[src + 1]; // R lo
      result[dst + 2] = data[src + 2]; // G hi
      result[dst + 3] = data[src + 3]; // G lo
      result[dst + 4] = data[src + 4]; // B hi
      result[dst + 5] = data[src + 5]; // B lo
    }

    return result;
  }

  /// <summary>Converts 16-bit big-endian grayscale (2 bytes/pixel) to RGB48 big-endian (6 bytes/pixel).</summary>
  public static byte[] Gray16ToRgb48(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 6];

    for (var i = 0; i < totalPixels; ++i) {
      var hi = data[i * 2];
      var lo = data[i * 2 + 1];
      var dst = i * 6;
      result[dst] = hi;
      result[dst + 1] = lo;
      result[dst + 2] = hi;
      result[dst + 3] = lo;
      result[dst + 4] = hi;
      result[dst + 5] = lo;
    }

    return result;
  }

  /// <summary>Converts 16-bit big-endian grayscale (2 bytes/pixel) to RGBA64 big-endian (8 bytes/pixel).</summary>
  public static byte[] Gray16ToRgba64(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 8];

    for (var i = 0; i < totalPixels; ++i) {
      var hi = data[i * 2];
      var lo = data[i * 2 + 1];
      var dst = i * 8;
      result[dst] = hi;
      result[dst + 1] = lo;
      result[dst + 2] = hi;
      result[dst + 3] = lo;
      result[dst + 4] = hi;
      result[dst + 5] = lo;
      result[dst + 6] = 0xFF;
      result[dst + 7] = 0xFF;
    }

    return result;
  }

  /// <summary>Converts RGB48 big-endian (6 bytes/pixel) to 16-bit big-endian grayscale (2 bytes/pixel) using luminance.</summary>
  public static byte[] Rgb48ToGray16(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 2];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 6;
      var r = (data[src] << 8) | data[src + 1];
      var g = (data[src + 2] << 8) | data[src + 3];
      var b = (data[src + 4] << 8) | data[src + 5];
      // Luminance: (R*19595 + G*38470 + B*7471) >> 16
      var gray = (r * 19595 + g * 38470 + b * 7471) >> 16;
      var dst = i * 2;
      result[dst] = (byte)(gray >> 8);
      result[dst + 1] = (byte)gray;
    }

    return result;
  }

  /// <summary>Converts RGBA64 big-endian (8 bytes/pixel) to 16-bit big-endian grayscale (2 bytes/pixel) using luminance, discarding alpha.</summary>
  public static byte[] Rgba64ToGray16(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 2];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 8;
      var r = (data[src] << 8) | data[src + 1];
      var g = (data[src + 2] << 8) | data[src + 3];
      var b = (data[src + 4] << 8) | data[src + 5];
      var gray = (r * 19595 + g * 38470 + b * 7471) >> 16;
      var dst = i * 2;
      result[dst] = (byte)(gray >> 8);
      result[dst + 1] = (byte)gray;
    }

    return result;
  }

  // ── Direct 16→8 shortcuts ─────────────────────────────────────────────────

  /// <summary>Converts RGB48 big-endian (6 bytes/pixel) to RGB24 (3 bytes/pixel) by taking high bytes.</summary>
  public static byte[] Rgb48ToRgb24(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 3];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 6;
      var dst = i * 3;
      result[dst] = data[src];     // R hi
      result[dst + 1] = data[src + 2]; // G hi
      result[dst + 2] = data[src + 4]; // B hi
    }

    return result;
  }

  /// <summary>Converts RGBA64 big-endian (8 bytes/pixel) to RGBA32 (4 bytes/pixel) by taking high bytes.</summary>
  public static byte[] Rgba64ToRgba32(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 4];
    var i = 0;

    if (Vector128.IsHardwareAccelerated) {
      // Process 4 pixels at a time: 32 bytes → 16 bytes (extract high bytes from each 16-bit pair)
      var shuffle = Vector128.Create((byte)0, 2, 4, 6, 8, 10, 12, 14, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80);
      for (; i <= totalPixels - 4; i += 4) {
        var src0 = Vector128.Create(data, i * 8);       // pixels 0-1 (16 bytes)
        var src1 = Vector128.Create(data, i * 8 + 16);  // pixels 2-3 (16 bytes)
        var lo = Vector128.Shuffle(src0, shuffle); // 8 useful bytes in low half
        var hi = Vector128.Shuffle(src1, shuffle); // 8 useful bytes in low half
        // Combine: lo bytes 0-7 → result 0-7, hi bytes 0-7 → result 8-15
        var combined = lo.WithUpper(hi.GetLower());
        combined.CopyTo(result, i * 4);
      }
    }

    for (; i < totalPixels; ++i) {
      var src = i * 8;
      var dst = i * 4;
      result[dst] = data[src];
      result[dst + 1] = data[src + 2];
      result[dst + 2] = data[src + 4];
      result[dst + 3] = data[src + 6];
    }

    return result;
  }

  /// <summary>Converts 16-bit big-endian grayscale (2 bytes/pixel) to 8-bit grayscale (1 byte/pixel) by taking the high byte.</summary>
  public static byte[] Gray16ToGray8(byte[] data, int totalPixels) {
    var result = new byte[totalPixels];

    for (var i = 0; i < totalPixels; ++i)
      result[i] = data[i * 2]; // high byte

    return result;
  }

  // ── Direct 8→16 upscale ───────────────────────────────────────────────────

  /// <summary>Converts RGB24 (3 bytes/pixel) to RGB48 big-endian (6 bytes/pixel). Upscales via v*257 = (v&lt;&lt;8)|v.</summary>
  public static byte[] Rgb24ToRgb48(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 6];

    for (var i = 0; i < totalPixels; ++i) {
      var src = i * 3;
      var dst = i * 6;
      var r = data[src];
      var g = data[src + 1];
      var b = data[src + 2];
      result[dst] = r;
      result[dst + 1] = r;
      result[dst + 2] = g;
      result[dst + 3] = g;
      result[dst + 4] = b;
      result[dst + 5] = b;
    }

    return result;
  }

  /// <summary>Converts RGBA32 (4 bytes/pixel) to RGBA64 big-endian (8 bytes/pixel). Upscales via v*257.</summary>
  public static byte[] Rgba32ToRgba64(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 8];
    var i = 0;

    if (Vector128.IsHardwareAccelerated) {
      // Process 4 pixels at a time: 16 bytes → 32 bytes (each byte duplicated)
      var shuffleLo = Vector128.Create((byte)0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7);
      var shuffleHi = Vector128.Create((byte)8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15);
      for (; i <= totalPixels - 4; i += 4) {
        var src = Vector128.Create(data, i * 4);
        Vector128.Shuffle(src, shuffleLo).CopyTo(result, i * 8);
        Vector128.Shuffle(src, shuffleHi).CopyTo(result, i * 8 + 16);
      }
    }

    for (; i < totalPixels; ++i) {
      var src = i * 4;
      var dst = i * 8;
      var r = data[src];
      var g = data[src + 1];
      var b = data[src + 2];
      var a = data[src + 3];
      result[dst] = r;
      result[dst + 1] = r;
      result[dst + 2] = g;
      result[dst + 3] = g;
      result[dst + 4] = b;
      result[dst + 5] = b;
      result[dst + 6] = a;
      result[dst + 7] = a;
    }

    return result;
  }

  /// <summary>Converts 8-bit grayscale (1 byte/pixel) to 16-bit big-endian grayscale (2 bytes/pixel). Upscales via v*257.</summary>
  public static byte[] Gray8ToGray16(byte[] data, int totalPixels) {
    var result = new byte[totalPixels * 2];
    var i = 0;

    if (Vector128.IsHardwareAccelerated) {
      // Process 16 pixels at a time: duplicate each byte (v → v,v)
      for (; i <= totalPixels - 16; i += 16) {
        var src = Vector128.Create(data, i);
        // Unpack low 8 bytes: interleave with self → 16 bytes (v0,v0,v1,v1,...)
        var lo = Vector128.Create(
          (byte)0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7);
        var hi = Vector128.Create(
          (byte)8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15);
        Vector128.Shuffle(src, lo).CopyTo(result, i * 2);
        Vector128.Shuffle(src, hi).CopyTo(result, i * 2 + 16);
      }
    }

    for (; i < totalPixels; ++i) {
      var v = data[i];
      result[i * 2] = v;
      result[i * 2 + 1] = v;
    }

    return result;
  }

  /// <summary>Converts band-sequential pixel data (all of band 0, then all of band 1, ...) to interleaved (pixel 0 band 0, pixel 0 band 1, ...).</summary>
  public static byte[] BandSequentialToInterleaved(byte[] bsq, int pixelCount, int bands) {
    var result = new byte[pixelCount * bands];

    if (bands == 3) {
      var i = 0;

      if (Vector128.IsHardwareAccelerated) {
        var band0Span = bsq.AsSpan(0, pixelCount);
        var band1Span = bsq.AsSpan(pixelCount, pixelCount);
        var band2Span = bsq.AsSpan(pixelCount * 2, pixelCount);
        var dstSpan = result.AsSpan();

        // Process 16 pixels per iteration → 48 output bytes (3 x Vector128)
        for (; i + 16 <= pixelCount; i += 16) {
          var r = Vector128.Create(band0Span.Slice(i, 16));
          var g = Vector128.Create(band1Span.Slice(i, 16));
          var b = Vector128.Create(band2Span.Slice(i, 16));

          // Interleave first 5 pixels: r0,g0,b0,r1,g1,b1,...r4,g4,b4 + 1 pad
          var m0r = Vector128.Create((byte)0, 0xFF, 0xFF, 1, 0xFF, 0xFF, 2, 0xFF, 0xFF, 3, 0xFF, 0xFF, 4, 0xFF, 0xFF, 0xFF);
          var m0g = Vector128.Create((byte)0xFF, 0, 0xFF, 0xFF, 1, 0xFF, 0xFF, 2, 0xFF, 0xFF, 3, 0xFF, 0xFF, 4, 0xFF, 0xFF);
          var m0b = Vector128.Create((byte)0xFF, 0xFF, 0, 0xFF, 0xFF, 1, 0xFF, 0xFF, 2, 0xFF, 0xFF, 3, 0xFF, 0xFF, 4, 0xFF);
          var out0 = Vector128.Shuffle(r, m0r) | Vector128.Shuffle(g, m0g) | Vector128.Shuffle(b, m0b);

          var dstBase = i * 3;
          out0.GetLower().CopyTo(dstSpan.Slice(dstBase, 8));
          dstSpan[dstBase + 8] = out0.GetElement(8);
          dstSpan[dstBase + 9] = out0.GetElement(9);
          dstSpan[dstBase + 10] = out0.GetElement(10);
          dstSpan[dstBase + 11] = out0.GetElement(11);
          dstSpan[dstBase + 12] = out0.GetElement(12);
          dstSpan[dstBase + 13] = out0.GetElement(13);
          dstSpan[dstBase + 14] = out0.GetElement(14);

          // Pixels 5-10
          var m1r = Vector128.Create((byte)5, 0xFF, 0xFF, 6, 0xFF, 0xFF, 7, 0xFF, 0xFF, 8, 0xFF, 0xFF, 9, 0xFF, 0xFF, 0xFF);
          var m1g = Vector128.Create((byte)0xFF, 5, 0xFF, 0xFF, 6, 0xFF, 0xFF, 7, 0xFF, 0xFF, 8, 0xFF, 0xFF, 9, 0xFF, 0xFF);
          var m1b = Vector128.Create((byte)0xFF, 0xFF, 5, 0xFF, 0xFF, 6, 0xFF, 0xFF, 7, 0xFF, 0xFF, 8, 0xFF, 0xFF, 9, 0xFF);
          var out1 = Vector128.Shuffle(r, m1r) | Vector128.Shuffle(g, m1g) | Vector128.Shuffle(b, m1b);

          out1.GetLower().CopyTo(dstSpan.Slice(dstBase + 15, 8));
          dstSpan[dstBase + 23] = out1.GetElement(8);
          dstSpan[dstBase + 24] = out1.GetElement(9);
          dstSpan[dstBase + 25] = out1.GetElement(10);
          dstSpan[dstBase + 26] = out1.GetElement(11);
          dstSpan[dstBase + 27] = out1.GetElement(12);
          dstSpan[dstBase + 28] = out1.GetElement(13);
          dstSpan[dstBase + 29] = out1.GetElement(14);

          // Pixels 10-15
          var m2r = Vector128.Create((byte)10, 0xFF, 0xFF, 11, 0xFF, 0xFF, 12, 0xFF, 0xFF, 13, 0xFF, 0xFF, 14, 0xFF, 0xFF, 0xFF);
          var m2g = Vector128.Create((byte)0xFF, 10, 0xFF, 0xFF, 11, 0xFF, 0xFF, 12, 0xFF, 0xFF, 13, 0xFF, 0xFF, 14, 0xFF, 0xFF);
          var m2b = Vector128.Create((byte)0xFF, 0xFF, 10, 0xFF, 0xFF, 11, 0xFF, 0xFF, 12, 0xFF, 0xFF, 13, 0xFF, 0xFF, 14, 0xFF);
          var out2 = Vector128.Shuffle(r, m2r) | Vector128.Shuffle(g, m2g) | Vector128.Shuffle(b, m2b);

          out2.GetLower().CopyTo(dstSpan.Slice(dstBase + 30, 8));
          dstSpan[dstBase + 38] = out2.GetElement(8);
          dstSpan[dstBase + 39] = out2.GetElement(9);
          dstSpan[dstBase + 40] = out2.GetElement(10);
          dstSpan[dstBase + 41] = out2.GetElement(11);
          dstSpan[dstBase + 42] = out2.GetElement(12);
          dstSpan[dstBase + 43] = out2.GetElement(13);
          dstSpan[dstBase + 44] = out2.GetElement(14);

          // Pixel 15 (scalar)
          dstSpan[dstBase + 45] = bsq[i + 15];
          dstSpan[dstBase + 46] = bsq[pixelCount + i + 15];
          dstSpan[dstBase + 47] = bsq[pixelCount * 2 + i + 15];
        }
      }

      for (; i < pixelCount; ++i) {
        result[i * 3] = bsq[i];
        result[i * 3 + 1] = bsq[pixelCount + i];
        result[i * 3 + 2] = bsq[pixelCount * 2 + i];
      }
    } else {
      for (var i = 0; i < pixelCount; ++i)
        for (var band = 0; band < bands; ++band)
          result[i * bands + band] = bsq[band * pixelCount + i];
    }

    return result;
  }

  /// <summary>Converts interleaved pixel data (pixel 0 band 0, pixel 0 band 1, ...) to band-sequential (all of band 0, then all of band 1, ...).</summary>
  public static byte[] InterleavedToBandSequential(byte[] interleaved, int pixelCount, int bands) {
    var result = new byte[pixelCount * bands];

    if (bands == 3) {
      var i = 0;

      if (Vector128.IsHardwareAccelerated) {
        var srcSpan = interleaved.AsSpan();
        var band0Span = result.AsSpan(0, pixelCount);
        var band1Span = result.AsSpan(pixelCount, pixelCount);
        var band2Span = result.AsSpan(pixelCount * 2, pixelCount);

        // Process 5 pixels per iteration: 15 input bytes → 5+5+5 output bytes
        for (; i + 16 <= pixelCount; i += 16) {
          var srcBase = i * 3;

          // Load first 16 bytes covering pixels 0-4 (and partial pixel 5)
          var in0 = Vector128.Create(srcSpan.Slice(srcBase, 16));
          var extractR0 = Vector128.Create((byte)0, 3, 6, 9, 12, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
          var extractG0 = Vector128.Create((byte)1, 4, 7, 10, 13, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
          var extractB0 = Vector128.Create((byte)2, 5, 8, 11, 14, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);

          var r0 = Vector128.Shuffle(in0, extractR0);
          var g0 = Vector128.Shuffle(in0, extractG0);
          var b0 = Vector128.Shuffle(in0, extractB0);

          // Pixels 5-10
          var in1 = Vector128.Create(srcSpan.Slice(srcBase + 15, 16));
          var r1 = Vector128.Shuffle(in1, extractR0);
          var g1 = Vector128.Shuffle(in1, extractG0);
          var b1 = Vector128.Shuffle(in1, extractB0);

          // Pixels 10-15
          var in2 = Vector128.Create(srcSpan.Slice(srcBase + 30, 16));
          var r2 = Vector128.Shuffle(in2, extractR0);
          var g2 = Vector128.Shuffle(in2, extractG0);
          var b2 = Vector128.Shuffle(in2, extractB0);

          // Pixel 15
          var r15 = interleaved[srcBase + 45];
          var g15 = interleaved[srcBase + 46];
          var b15 = interleaved[srcBase + 47];

          // Write 16 bytes to each band
          band0Span[i] = r0.GetElement(0);
          band0Span[i + 1] = r0.GetElement(1);
          band0Span[i + 2] = r0.GetElement(2);
          band0Span[i + 3] = r0.GetElement(3);
          band0Span[i + 4] = r0.GetElement(4);
          band0Span[i + 5] = r1.GetElement(0);
          band0Span[i + 6] = r1.GetElement(1);
          band0Span[i + 7] = r1.GetElement(2);
          band0Span[i + 8] = r1.GetElement(3);
          band0Span[i + 9] = r1.GetElement(4);
          band0Span[i + 10] = r2.GetElement(0);
          band0Span[i + 11] = r2.GetElement(1);
          band0Span[i + 12] = r2.GetElement(2);
          band0Span[i + 13] = r2.GetElement(3);
          band0Span[i + 14] = r2.GetElement(4);
          band0Span[i + 15] = r15;

          band1Span[i] = g0.GetElement(0);
          band1Span[i + 1] = g0.GetElement(1);
          band1Span[i + 2] = g0.GetElement(2);
          band1Span[i + 3] = g0.GetElement(3);
          band1Span[i + 4] = g0.GetElement(4);
          band1Span[i + 5] = g1.GetElement(0);
          band1Span[i + 6] = g1.GetElement(1);
          band1Span[i + 7] = g1.GetElement(2);
          band1Span[i + 8] = g1.GetElement(3);
          band1Span[i + 9] = g1.GetElement(4);
          band1Span[i + 10] = g2.GetElement(0);
          band1Span[i + 11] = g2.GetElement(1);
          band1Span[i + 12] = g2.GetElement(2);
          band1Span[i + 13] = g2.GetElement(3);
          band1Span[i + 14] = g2.GetElement(4);
          band1Span[i + 15] = g15;

          band2Span[i] = b0.GetElement(0);
          band2Span[i + 1] = b0.GetElement(1);
          band2Span[i + 2] = b0.GetElement(2);
          band2Span[i + 3] = b0.GetElement(3);
          band2Span[i + 4] = b0.GetElement(4);
          band2Span[i + 5] = b1.GetElement(0);
          band2Span[i + 6] = b1.GetElement(1);
          band2Span[i + 7] = b1.GetElement(2);
          band2Span[i + 8] = b1.GetElement(3);
          band2Span[i + 9] = b1.GetElement(4);
          band2Span[i + 10] = b2.GetElement(0);
          band2Span[i + 11] = b2.GetElement(1);
          band2Span[i + 12] = b2.GetElement(2);
          band2Span[i + 13] = b2.GetElement(3);
          band2Span[i + 14] = b2.GetElement(4);
          band2Span[i + 15] = b15;
        }
      }

      for (; i < pixelCount; ++i) {
        result[i] = interleaved[i * 3];
        result[pixelCount + i] = interleaved[i * 3 + 1];
        result[pixelCount * 2 + i] = interleaved[i * 3 + 2];
      }
    } else {
      for (var i = 0; i < pixelCount; ++i)
        for (var band = 0; band < bands; ++band)
          result[band * pixelCount + i] = interleaved[i * bands + band];
    }

    return result;
  }

  // ── Shared SIMD helpers ───────────────────────────────────────────────────

  /// <summary>Shuffles 4-byte pixels in-place using Vector128/Vector256 with a given mask. Scalar fallback uses per-byte mapping.</summary>
  private static byte[] _Shuffle4To4(byte[] data, int totalPixels, Vector128<byte> mask, byte map0, byte map1, byte map2, byte map3) {
    var result = new byte[totalPixels * 4];
    var byteLen = totalPixels * 4;
    var i = 0;

    if (Vector128.IsHardwareAccelerated) {
      var srcSpan = data.AsSpan();
      var dstSpan = result.AsSpan();

      if (Vector256.IsHardwareAccelerated) {
        var mask256 = Vector256.Create(mask, mask);
        for (; i + 32 <= byteLen; i += 32) {
          var vec = Vector256.Create(srcSpan.Slice(i, 32));
          Vector256.Shuffle(vec, mask256).CopyTo(dstSpan.Slice(i, 32));
        }
      }

      for (; i + 16 <= byteLen; i += 16) {
        var vec = Vector128.Create(srcSpan.Slice(i, 16));
        Vector128.Shuffle(vec, mask).CopyTo(dstSpan.Slice(i, 16));
      }
    }

    for (; i < byteLen; i += 4) {
      result[i] = data[i + map0];
      result[i + 1] = data[i + map1];
      result[i + 2] = data[i + map2];
      result[i + 3] = data[i + map3];
    }

    return result;
  }

  /// <summary>Expands 3-byte pixels to 4-byte pixels with alpha=255 using Vector128 shuffle.</summary>
  private static byte[] _Expand3To4(byte[] data, int totalPixels, Vector128<byte> expandMask, byte map0, byte map1, byte map2) {
    var result = new byte[totalPixels * 4];
    var srcOffset = 0;
    var dstOffset = 0;

    if (Vector128.IsHardwareAccelerated) {
      var alphaMask = Vector128.Create((byte)0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);
      var srcSpan = data.AsSpan();
      var dstSpan = result.AsSpan();

      while (srcOffset + 16 <= data.Length && dstOffset + 16 <= result.Length) {
        var vec = Vector128.Create(srcSpan.Slice(srcOffset, 16));
        (Vector128.Shuffle(vec, expandMask) | alphaMask).CopyTo(dstSpan.Slice(dstOffset, 16));
        srcOffset += 12;
        dstOffset += 16;
      }
    }

    var pixel = srcOffset / 3;
    for (; pixel < totalPixels; ++pixel) {
      var src = pixel * 3;
      var dst = pixel * 4;
      result[dst] = data[src + map0];
      result[dst + 1] = data[src + map1];
      result[dst + 2] = data[src + map2];
      result[dst + 3] = 255;
    }

    return result;
  }

  /// <summary>Compacts 4-byte pixels to 3-byte pixels (drops alpha) using Vector128 shuffle.</summary>
  private static byte[] _Compact4To3(byte[] data, int totalPixels, Vector128<byte> compactMask, byte map0, byte map1, byte map2) {
    var result = new byte[totalPixels * 3];
    var srcOffset = 0;
    var dstOffset = 0;

    if (Vector128.IsHardwareAccelerated) {
      var srcSpan = data.AsSpan();
      var dstSpan = result.AsSpan();

      while (srcOffset + 16 <= data.Length && dstOffset + 12 <= result.Length) {
        var vec = Vector128.Create(srcSpan.Slice(srcOffset, 16));
        var compacted = Vector128.Shuffle(vec, compactMask);
        compacted.GetLower().CopyTo(dstSpan.Slice(dstOffset, 8));
        dstSpan[dstOffset + 8] = compacted.GetElement(8);
        dstSpan[dstOffset + 9] = compacted.GetElement(9);
        dstSpan[dstOffset + 10] = compacted.GetElement(10);
        dstSpan[dstOffset + 11] = compacted.GetElement(11);
        srcOffset += 16;
        dstOffset += 12;
      }
    }

    var pixel = srcOffset / 4;
    for (; pixel < totalPixels; ++pixel) {
      var src = pixel * 4;
      var dst = pixel * 3;
      result[dst] = data[src + map0];
      result[dst + 1] = data[src + map1];
      result[dst + 2] = data[src + map2];
    }

    return result;
  }
}
