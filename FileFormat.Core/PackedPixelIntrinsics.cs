using System;
using System.Buffers.Binary;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace FileFormat.Core;

/// <summary>
/// Architecture-specific packed-pixel fast paths used before the portable converter.
/// </summary>
/// <remarks>
/// Keep this class narrow: <c>pshufb</c> is excellent for byte-channel permutations and BMI2
/// <c>pext</c> is excellent when the target really is a selection of sparse source bits. More general
/// arithmetic remains in <see cref="PixelConverter"/> / <see cref="FastRawImageConverter"/> so CPUs
/// without these instructions keep the same behaviour.
/// </remarks>
internal static class PackedPixelIntrinsics {

  private const uint _BgraToRgb565PextMask = 0x00F8FCF8u;

  private static readonly Vector128<byte> _OpaqueAlpha = Vector128.Create(
    (byte)0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255);

  private static readonly Vector128<byte> _Rgb24ToBgra = Vector128.Create(
    (byte)2, 1, 0, 0x80, 5, 4, 3, 0x80, 8, 7, 6, 0x80, 11, 10, 9, 0x80);
  private static readonly Vector128<byte> _Bgr24ToBgra = Vector128.Create(
    (byte)0, 1, 2, 0x80, 3, 4, 5, 0x80, 6, 7, 8, 0x80, 9, 10, 11, 0x80);
  private static readonly Vector128<byte> _RgbaToBgra = Vector128.Create(
    (byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
  private static readonly Vector128<byte> _ArgbToBgra = Vector128.Create(
    (byte)3, 2, 1, 0, 7, 6, 5, 4, 11, 10, 9, 8, 15, 14, 13, 12);

  private static readonly Vector128<byte> _BgraToRgb24 = Vector128.Create(
    (byte)2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, 0x80, 0x80, 0x80, 0x80);
  private static readonly Vector128<byte> _BgraToBgr24 = Vector128.Create(
    (byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0x80, 0x80, 0x80, 0x80);
  private static readonly Vector128<byte> _RgbaToRgb24 = Vector128.Create(
    (byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0x80, 0x80, 0x80, 0x80);

  /// <summary>Attempts a conversion for which the current CPU has a worthwhile specialized path.</summary>
  public static bool TryConvert(RawImage source, PixelFormat target, out RawImage converted) {
    ArgumentNullException.ThrowIfNull(source);

    var pixels = checked(source.Width * source.Height);
    byte[]? bytes = null;

    if (Ssse3.IsSupported) {
      bytes = (source.Format, target) switch {
        (PixelFormat.Rgb24, PixelFormat.Bgra32) => _Expand3ToBgra(source.PixelData, pixels, _Rgb24ToBgra, 2, 1, 0),
        (PixelFormat.Bgr24, PixelFormat.Bgra32) => _Expand3ToBgra(source.PixelData, pixels, _Bgr24ToBgra, 0, 1, 2),
        (PixelFormat.Rgba32, PixelFormat.Bgra32) => _Shuffle4(source.PixelData, pixels, _RgbaToBgra, 2, 1, 0, 3),
        (PixelFormat.Argb32, PixelFormat.Bgra32) => _Shuffle4(source.PixelData, pixels, _ArgbToBgra, 3, 2, 1, 0),
        (PixelFormat.Bgra32, PixelFormat.Rgba32) => _Shuffle4(source.PixelData, pixels, _RgbaToBgra, 2, 1, 0, 3),
        (PixelFormat.Bgra32, PixelFormat.Argb32) => _Shuffle4(source.PixelData, pixels, _ArgbToBgra, 3, 2, 1, 0),
        (PixelFormat.Bgra32, PixelFormat.Rgb24) => _Compact4To3(source.PixelData, pixels, _BgraToRgb24, 2, 1, 0),
        (PixelFormat.Bgra32, PixelFormat.Bgr24) => _Compact4To3(source.PixelData, pixels, _BgraToBgr24, 0, 1, 2),
        (PixelFormat.Rgba32, PixelFormat.Rgb24) => _Compact4To3(source.PixelData, pixels, _RgbaToRgb24, 0, 1, 2),
        _ => null,
      };
    }

    if (bytes == null && Bmi2.IsSupported && source.Format == PixelFormat.Bgra32 && target == PixelFormat.Rgb565)
      bytes = _BgraToRgb565Pext(source.PixelData, pixels);

    if (bytes == null) {
      converted = null!;
      return false;
    }

    converted = new RawImage {
      Width = source.Width,
      Height = source.Height,
      Format = target,
      PixelData = bytes,
      ColorInfo = source.ColorInfo,
      Metadata = source.Metadata,
    };
    return true;
  }

  private static byte[] _Shuffle4(byte[] source, int pixels, Vector128<byte> mask, int c0, int c1, int c2, int c3) {
    var required = checked(pixels * 4);
    if (source.Length < required)
      throw new InvalidOperationException("Packed 32-bit pixel buffer is shorter than its declared dimensions.");

    var result = new byte[required];
    var i = 0;
    for (; i + 4 <= pixels; i += 4) {
      var value = Vector128.Create(source.AsSpan(i * 4, 16));
      Ssse3.Shuffle(value, mask).CopyTo(result, i * 4);
    }

    for (; i < pixels; ++i) {
      var src = i * 4;
      var dst = src;
      result[dst] = source[src + c0];
      result[dst + 1] = source[src + c1];
      result[dst + 2] = source[src + c2];
      result[dst + 3] = source[src + c3];
    }

    return result;
  }

  private static byte[] _Compact4To3(byte[] source, int pixels, Vector128<byte> mask, int c0, int c1, int c2) {
    var required = checked(pixels * 4);
    if (source.Length < required)
      throw new InvalidOperationException("Packed 32-bit pixel buffer is shorter than its declared dimensions.");

    var result = new byte[checked(pixels * 3)];
    var i = 0;
    for (; i + 4 <= pixels; i += 4) {
      var shuffled = Ssse3.Shuffle(Vector128.Create(source.AsSpan(i * 4, 16)), mask);
      var dst = i * 3;
      shuffled.GetLower().CopyTo(result.AsSpan(dst, 8));
      BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(dst + 8, 4), shuffled.AsUInt32().GetElement(2));
    }

    for (; i < pixels; ++i) {
      var src = i * 4;
      var dst = i * 3;
      result[dst] = source[src + c0];
      result[dst + 1] = source[src + c1];
      result[dst + 2] = source[src + c2];
    }

    return result;
  }

  private static byte[] _Expand3ToBgra(byte[] source, int pixels, Vector128<byte> mask, int b, int g, int r) {
    var required = checked(pixels * 3);
    if (source.Length < required)
      throw new InvalidOperationException("Packed 24-bit pixel buffer is shorter than its declared dimensions.");

    var result = new byte[checked(pixels * 4)];
    var i = 0;

    // A 128-bit load spans 16 bytes while four RGB/BGR pixels occupy 12. Requiring six remaining
    // pixels keeps every load inside the managed array without unsafe over-read at the tail.
    for (; i + 6 <= pixels; i += 4) {
      var shuffled = Ssse3.Shuffle(Vector128.Create(source.AsSpan(i * 3, 16)), mask);
      (shuffled | _OpaqueAlpha).CopyTo(result, i * 4);
    }

    for (; i < pixels; ++i) {
      var src = i * 3;
      var dst = i * 4;
      result[dst] = source[src + b];
      result[dst + 1] = source[src + g];
      result[dst + 2] = source[src + r];
      result[dst + 3] = 255;
    }

    return result;
  }

  private static byte[] _BgraToRgb565Pext(byte[] source, int pixels) {
    var required = checked(pixels * 4);
    if (source.Length < required)
      throw new InvalidOperationException("BGRA32 pixel buffer is shorter than its declared dimensions.");

    var result = new byte[checked(pixels * 2)];
    var i = 0;

    // In a little-endian BGRA uint, the wanted top bits occupy B[7:3], G[7:2], R[7:3]. PEXT visits
    // source bits from low to high, so this mask directly produces bbbbb_gggggg_rrrrr in the output
    // bit positions required by little-endian RGB565: B in 0..4, G in 5..10, R in 11..15.
    for (; i + 4 <= pixels; i += 4) {
      var src = i * 4;
      var dst = i * 2;
      var p0 = Bmi2.ParallelBitExtract(BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(src, 4)), _BgraToRgb565PextMask);
      var p1 = Bmi2.ParallelBitExtract(BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(src + 4, 4)), _BgraToRgb565PextMask);
      var p2 = Bmi2.ParallelBitExtract(BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(src + 8, 4)), _BgraToRgb565PextMask);
      var p3 = Bmi2.ParallelBitExtract(BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(src + 12, 4)), _BgraToRgb565PextMask);
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(dst, 2), (ushort)p0);
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(dst + 2, 2), (ushort)p1);
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(dst + 4, 2), (ushort)p2);
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(dst + 6, 2), (ushort)p3);
    }

    for (; i < pixels; ++i) {
      var src = i * 4;
      var packed = Bmi2.ParallelBitExtract(BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(src, 4)), _BgraToRgb565PextMask);
      BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i * 2, 2), (ushort)packed);
    }

    return result;
  }
}
