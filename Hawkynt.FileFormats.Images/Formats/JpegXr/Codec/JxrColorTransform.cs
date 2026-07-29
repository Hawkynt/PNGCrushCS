using System;
using System.Runtime.CompilerServices;

namespace FileFormat.JpegXr.Codec;

/// <summary>
/// Color space transformation engine for JPEG XR (ITU-T T.832).
/// Supports the following color transforms:
/// - YCoCg (reversible integer lossless transform, default for RGB)
/// - YUV (BT.601 / BT.709 for interop, lossy)
/// - CMYK to/from RGB (4-channel support)
/// - Identity (no transform, for grayscale or pre-transformed data)
/// </summary>
/// <remarks>
/// JPEG XR's primary internal color space is YCoCg, a reversible integer transform
/// that decorrelates RGB channels with no rounding loss. This is one of the key
/// features enabling JPEG XR's lossless mode.
///
/// The transform operates on individual pixels or bulk macroblock data:
/// - Forward: RGB -> YCoCg (applied before the spatial transform in the encoder)
/// - Inverse: YCoCg -> RGB (applied after the spatial transform in the decoder)
///
/// For multi-channel images (e.g. CMYK), the first 3 channels can be color-transformed
/// while the 4th channel (K) is coded independently.
/// </remarks>
internal static class JxrColorTransform {

  /// <summary>Supported color transform modes.</summary>
  internal enum Mode : byte {
    /// <summary>No color transform (grayscale, or data already in desired space).</summary>
    Identity = 0,

    /// <summary>Reversible YCoCg integer transform (default for RGB in JPEG XR).</summary>
    YCoCg = 1,

    /// <summary>YCbCr BT.601 (lossy, for interop with JPEG/MPEG).</summary>
    YCbCr601 = 2,

    /// <summary>YCbCr BT.709 (lossy, for HD content).</summary>
    YCbCr709 = 3
  }

  #region YCoCg (Reversible Integer Transform)

  /// <summary>
  /// Forward YCoCg color transform for a single pixel.
  /// This is the JPEG XR primary color transform: perfectly reversible with integer arithmetic.
  /// R,G,B in [0..255] produce Y in [0..255], Co/Cg in approximately [-128..127].
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void ForwardYCoCg(int r, int g, int b, out int y, out int co, out int cg) {
    co = r - b;
    var tmp = b + (co >> 1);
    cg = g - tmp;
    y = tmp + (cg >> 1);
  }

  /// <summary>
  /// Inverse YCoCg color transform for a single pixel.
  /// Perfectly reconstructs the original R,G,B values.
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void InverseYCoCg(int y, int co, int cg, out int r, out int g, out int b) {
    var tmp = y - (cg >> 1);
    g = tmp + cg;
    b = tmp - (co >> 1);
    r = b + co;
  }

  /// <summary>
  /// Applies forward YCoCg to an entire macroblock (bulk transform).
  /// Input: separate R, G, B channel arrays (each 256 elements for 16x16).
  /// Output: Y, Co, Cg channel arrays.
  /// </summary>
  public static void ForwardYCoCgBlock(
    ReadOnlySpan<int> rCh, ReadOnlySpan<int> gCh, ReadOnlySpan<int> bCh,
    Span<int> yCh, Span<int> coCh, Span<int> cgCh
  ) {
    var count = Math.Min(rCh.Length, Math.Min(gCh.Length, bCh.Length));
    for (var i = 0; i < count; ++i)
      ForwardYCoCg(rCh[i], gCh[i], bCh[i], out yCh[i], out coCh[i], out cgCh[i]);
  }

  /// <summary>
  /// Applies inverse YCoCg to an entire macroblock.
  /// </summary>
  public static void InverseYCoCgBlock(
    ReadOnlySpan<int> yCh, ReadOnlySpan<int> coCh, ReadOnlySpan<int> cgCh,
    Span<int> rCh, Span<int> gCh, Span<int> bCh
  ) {
    var count = Math.Min(yCh.Length, Math.Min(coCh.Length, cgCh.Length));
    for (var i = 0; i < count; ++i)
      InverseYCoCg(yCh[i], coCh[i], cgCh[i], out rCh[i], out gCh[i], out bCh[i]);
  }

  #endregion

  #region YCbCr BT.601

  /// <summary>
  /// Forward YCbCr BT.601 transform (lossy, fixed-point approximation).
  /// Uses 16-bit fixed-point arithmetic (multiply by 256, shift right by 8).
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void ForwardYCbCr601(int r, int g, int b, out int y, out int cb, out int cr) {
    // Y  =  0.299*R + 0.587*G + 0.114*B
    // Cb = -0.169*R - 0.331*G + 0.500*B + 128
    // Cr =  0.500*R - 0.419*G - 0.081*B + 128
    y = (77 * r + 150 * g + 29 * b + 128) >> 8;
    cb = ((-43 * r - 85 * g + 128 * b + 128) >> 8) + 128;
    cr = ((128 * r - 107 * g - 21 * b + 128) >> 8) + 128;
  }

  /// <summary>
  /// Inverse YCbCr BT.601 transform (lossy).
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void InverseYCbCr601(int y, int cb, int cr, out int r, out int g, out int b) {
    // R = Y + 1.402 * (Cr - 128)
    // G = Y - 0.344 * (Cb - 128) - 0.714 * (Cr - 128)
    // B = Y + 1.772 * (Cb - 128)
    var cbShifted = cb - 128;
    var crShifted = cr - 128;
    r = y + ((359 * crShifted + 128) >> 8);
    g = y - ((88 * cbShifted + 183 * crShifted + 128) >> 8);
    b = y + ((454 * cbShifted + 128) >> 8);
  }

  /// <summary>Applies forward YCbCr BT.601 to a macroblock.</summary>
  public static void ForwardYCbCr601Block(
    ReadOnlySpan<int> rCh, ReadOnlySpan<int> gCh, ReadOnlySpan<int> bCh,
    Span<int> yCh, Span<int> cbCh, Span<int> crCh
  ) {
    var count = Math.Min(rCh.Length, Math.Min(gCh.Length, bCh.Length));
    for (var i = 0; i < count; ++i)
      ForwardYCbCr601(rCh[i], gCh[i], bCh[i], out yCh[i], out cbCh[i], out crCh[i]);
  }

  /// <summary>Applies inverse YCbCr BT.601 to a macroblock.</summary>
  public static void InverseYCbCr601Block(
    ReadOnlySpan<int> yCh, ReadOnlySpan<int> cbCh, ReadOnlySpan<int> crCh,
    Span<int> rCh, Span<int> gCh, Span<int> bCh
  ) {
    var count = Math.Min(yCh.Length, Math.Min(cbCh.Length, crCh.Length));
    for (var i = 0; i < count; ++i)
      InverseYCbCr601(yCh[i], cbCh[i], crCh[i], out rCh[i], out gCh[i], out bCh[i]);
  }

  #endregion

  #region YCbCr BT.709

  /// <summary>
  /// Forward YCbCr BT.709 transform (lossy, fixed-point).
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void ForwardYCbCr709(int r, int g, int b, out int y, out int cb, out int cr) {
    // Y  = 0.2126*R + 0.7152*G + 0.0722*B
    // Cb = -0.1146*R - 0.3854*G + 0.5000*B + 128
    // Cr = 0.5000*R - 0.4542*G - 0.0458*B + 128
    y = (54 * r + 183 * g + 18 * b + 128) >> 8;
    cb = ((-29 * r - 99 * g + 128 * b + 128) >> 8) + 128;
    cr = ((128 * r - 116 * g - 12 * b + 128) >> 8) + 128;
  }

  /// <summary>
  /// Inverse YCbCr BT.709 transform (lossy).
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void InverseYCbCr709(int y, int cb, int cr, out int r, out int g, out int b) {
    var cbShifted = cb - 128;
    var crShifted = cr - 128;
    // R = Y + 1.5748 * Cr'
    // G = Y - 0.1873 * Cb' - 0.4681 * Cr'
    // B = Y + 1.8556 * Cb'
    r = y + ((403 * crShifted + 128) >> 8);
    g = y - ((48 * cbShifted + 120 * crShifted + 128) >> 8);
    b = y + ((475 * cbShifted + 128) >> 8);
  }

  /// <summary>Applies forward YCbCr BT.709 to a macroblock.</summary>
  public static void ForwardYCbCr709Block(
    ReadOnlySpan<int> rCh, ReadOnlySpan<int> gCh, ReadOnlySpan<int> bCh,
    Span<int> yCh, Span<int> cbCh, Span<int> crCh
  ) {
    var count = Math.Min(rCh.Length, Math.Min(gCh.Length, bCh.Length));
    for (var i = 0; i < count; ++i)
      ForwardYCbCr709(rCh[i], gCh[i], bCh[i], out yCh[i], out cbCh[i], out crCh[i]);
  }

  /// <summary>Applies inverse YCbCr BT.709 to a macroblock.</summary>
  public static void InverseYCbCr709Block(
    ReadOnlySpan<int> yCh, ReadOnlySpan<int> cbCh, ReadOnlySpan<int> crCh,
    Span<int> rCh, Span<int> gCh, Span<int> bCh
  ) {
    var count = Math.Min(yCh.Length, Math.Min(cbCh.Length, crCh.Length));
    for (var i = 0; i < count; ++i)
      InverseYCbCr709(yCh[i], cbCh[i], crCh[i], out rCh[i], out gCh[i], out bCh[i]);
  }

  #endregion

  #region CMYK conversion

  /// <summary>
  /// Converts CMYK pixel values to RGB using the standard subtractive model.
  /// C, M, Y, K are in [0..255] where 0 = no ink, 255 = full ink.
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void CmykToRgb(int c, int m, int y, int k, out int r, out int g, out int b) {
    // R = (255 - C) * (255 - K) / 255
    // G = (255 - M) * (255 - K) / 255
    // B = (255 - Y) * (255 - K) / 255
    var kInv = 255 - k;
    r = ((255 - c) * kInv + 127) / 255;
    g = ((255 - m) * kInv + 127) / 255;
    b = ((255 - y) * kInv + 127) / 255;
  }

  /// <summary>
  /// Converts RGB pixel values to CMYK using the standard model with under-color removal.
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static void RgbToCmyk(int r, int g, int b, out int c, out int m, out int y, out int k) {
    // K = 255 - max(R, G, B)
    var maxRgb = Math.Max(r, Math.Max(g, b));
    if (maxRgb == 0) {
      c = 0;
      m = 0;
      y = 0;
      k = 255;
      return;
    }

    k = 255 - maxRgb;
    c = ((maxRgb - r) * 255 + maxRgb / 2) / maxRgb;
    m = ((maxRgb - g) * 255 + maxRgb / 2) / maxRgb;
    y = ((maxRgb - b) * 255 + maxRgb / 2) / maxRgb;
  }

  /// <summary>Applies CMYK to RGB conversion to a macroblock's 4 channels, producing 3 RGB channels.</summary>
  public static void CmykToRgbBlock(
    ReadOnlySpan<int> cCh, ReadOnlySpan<int> mCh, ReadOnlySpan<int> yCh, ReadOnlySpan<int> kCh,
    Span<int> rCh, Span<int> gCh, Span<int> bCh
  ) {
    var count = Math.Min(cCh.Length, Math.Min(mCh.Length, Math.Min(yCh.Length, kCh.Length)));
    for (var i = 0; i < count; ++i)
      CmykToRgb(cCh[i], mCh[i], yCh[i], kCh[i], out rCh[i], out gCh[i], out bCh[i]);
  }

  #endregion

  #region Dispatcher

  /// <summary>
  /// Applies the forward color transform to a macroblock's 3 channels in-place.
  /// Input channels contain RGB; output channels will contain the transformed color space.
  /// </summary>
  public static void ForwardTransform(Mode mode, int[][] channels) {
    if (channels.Length < 3 || mode == Mode.Identity)
      return;

    var count = channels[0].Length;
    var temp0 = new int[count];
    var temp1 = new int[count];
    var temp2 = new int[count];

    switch (mode) {
      case Mode.YCoCg:
        ForwardYCoCgBlock(channels[0], channels[1], channels[2], temp0, temp1, temp2);
        break;
      case Mode.YCbCr601:
        ForwardYCbCr601Block(channels[0], channels[1], channels[2], temp0, temp1, temp2);
        break;
      case Mode.YCbCr709:
        ForwardYCbCr709Block(channels[0], channels[1], channels[2], temp0, temp1, temp2);
        break;
    }

    temp0.AsSpan().CopyTo(channels[0]);
    temp1.AsSpan().CopyTo(channels[1]);
    temp2.AsSpan().CopyTo(channels[2]);
  }

  /// <summary>
  /// Applies the inverse color transform to a macroblock's 3 channels in-place.
  /// Input channels contain the transformed space; output channels will contain RGB.
  /// </summary>
  public static void InverseTransform(Mode mode, int[][] channels) {
    if (channels.Length < 3 || mode == Mode.Identity)
      return;

    var count = channels[0].Length;
    var temp0 = new int[count];
    var temp1 = new int[count];
    var temp2 = new int[count];

    switch (mode) {
      case Mode.YCoCg:
        InverseYCoCgBlock(channels[0], channels[1], channels[2], temp0, temp1, temp2);
        break;
      case Mode.YCbCr601:
        InverseYCbCr601Block(channels[0], channels[1], channels[2], temp0, temp1, temp2);
        break;
      case Mode.YCbCr709:
        InverseYCbCr709Block(channels[0], channels[1], channels[2], temp0, temp1, temp2);
        break;
    }

    temp0.AsSpan().CopyTo(channels[0]);
    temp1.AsSpan().CopyTo(channels[1]);
    temp2.AsSpan().CopyTo(channels[2]);
  }

  #endregion

  #region Pixel output helpers

  /// <summary>
  /// Converts a macroblock's channel data to interleaved RGB24 bytes and stores in the output buffer.
  /// Handles clamping to [0..255] and boundary clipping for partial macroblocks.
  /// </summary>
  public static void StoreRgb24(
    int[] rMb, int[] gMb, int[] bMb,
    byte[] output, int mbX, int mbY, int imgWidth, int imgHeight
  ) {
    var startX = mbX * 16;
    var startY = mbY * 16;

    for (var py = 0; py < 16; ++py) {
      var iy = startY + py;
      if (iy >= imgHeight)
        break;

      for (var px = 0; px < 16; ++px) {
        var ix = startX + px;
        if (ix >= imgWidth)
          break;

        var srcIdx = py * 16 + px;
        var dstIdx = (iy * imgWidth + ix) * 3;
        output[dstIdx] = ClampByte(rMb[srcIdx]);
        output[dstIdx + 1] = ClampByte(gMb[srcIdx]);
        output[dstIdx + 2] = ClampByte(bMb[srcIdx]);
      }
    }
  }

  /// <summary>
  /// Stores a grayscale macroblock into the output buffer.
  /// </summary>
  public static void StoreGray8(int[] mb, byte[] output, int mbX, int mbY, int imgWidth, int imgHeight) {
    var startX = mbX * 16;
    var startY = mbY * 16;

    for (var py = 0; py < 16; ++py) {
      var iy = startY + py;
      if (iy >= imgHeight)
        break;

      for (var px = 0; px < 16; ++px) {
        var ix = startX + px;
        if (ix >= imgWidth)
          break;

        output[iy * imgWidth + ix] = ClampByte(mb[py * 16 + px]);
      }
    }
  }

  /// <summary>
  /// Extracts RGB pixels from the image buffer into separate channel arrays for one macroblock.
  /// Pads with edge extension when the macroblock extends beyond the image boundary.
  /// </summary>
  public static void ExtractRgb24(
    byte[] pixels, int imgWidth, int imgHeight,
    int mbX, int mbY,
    int[] rCh, int[] gCh, int[] bCh
  ) {
    var startX = mbX * 16;
    var startY = mbY * 16;

    for (var py = 0; py < 16; ++py) {
      var iy = Math.Min(startY + py, imgHeight - 1);
      for (var px = 0; px < 16; ++px) {
        var ix = Math.Min(startX + px, imgWidth - 1);
        var srcIdx = (iy * imgWidth + ix) * 3;
        var dstIdx = py * 16 + px;
        rCh[dstIdx] = pixels[srcIdx];
        gCh[dstIdx] = pixels[srcIdx + 1];
        bCh[dstIdx] = pixels[srcIdx + 2];
      }
    }
  }

  /// <summary>
  /// Extracts grayscale pixels into a channel array for one macroblock.
  /// </summary>
  public static void ExtractGray8(byte[] pixels, int imgWidth, int imgHeight, int mbX, int mbY, int[] ch) {
    var startX = mbX * 16;
    var startY = mbY * 16;

    for (var py = 0; py < 16; ++py) {
      var iy = Math.Min(startY + py, imgHeight - 1);
      for (var px = 0; px < 16; ++px) {
        var ix = Math.Min(startX + px, imgWidth - 1);
        ch[py * 16 + px] = pixels[iy * imgWidth + ix];
      }
    }
  }

  /// <summary>Clamps an integer to the [0..255] byte range.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

  #endregion
}
