using System;
using System.IO;
using FileFormat.Codecs.H265;

namespace FileFormat.Bpg.Codec;

/// <summary>Converts reconstructed BPG component planes to the package's packed 8-bit output.</summary>
internal static class BpgColorConverter {

  internal static byte[] Convert(H265Picture picture, BpgFile bpg) {
    if (bpg.Width > picture.Width || bpg.Height > picture.Height)
      throw new InvalidDataException(
        $"The BPG container declares {bpg.Width}x{bpg.Height}, but the reconstructed HEVC picture is only {picture.Width}x{picture.Height}.");

    if (bpg.PixelFormat == BpgPixelFormat.Grayscale)
      return _Gray(picture, bpg);

    if (picture.IsMonochrome)
      throw new InvalidDataException("A color BPG picture reconstructed as monochrome HEVC.");

    if (bpg.ColorSpace == BpgColorSpace.Rgb && bpg.PixelFormat != BpgPixelFormat.YCbCr444)
      throw new NotSupportedException("BPG RGB component storage is implemented only for its 4:4:4 form.");

    var result = new byte[checked(bpg.Width * bpg.Height * 3)];
    var depth = bpg.BitDepth;
    var max = (1 << depth) - 1;
    var scale = 1 << (depth - 8);
    var lumaBlack = bpg.LimitedRange ? 16 * scale : 0;
    var lumaSpan = bpg.LimitedRange ? 219 * scale : max;
    var chromaCentre = 1 << (depth - 1);
    var chromaSpan = bpg.LimitedRange ? 224 * scale : max;
    var centeredHorizontally = bpg.PixelFormat is BpgPixelFormat.YCbCr420 or BpgPixelFormat.YCbCr422;

    var at = 0;
    for (var y = 0; y < bpg.Height; ++y)
      for (var x = 0; x < bpg.Width; ++x) {
        var luma = picture.Luma[y * picture.Width + x];
        var cb = _Chroma(picture.Cb, picture.ChromaWidth, x, y, picture.ChromaShiftX, picture.ChromaShiftY,
          bpg.Width, bpg.Height, centeredHorizontally);
        var cr = _Chroma(picture.Cr, picture.ChromaWidth, x, y, picture.ChromaShiftX, picture.ChromaShiftY,
          bpg.Width, bpg.Height, centeredHorizontally);

        _ToRgb(bpg.ColorSpace, bpg.LimitedRange, luma, cb, cr,
          max, lumaBlack, lumaSpan, chromaCentre, chromaSpan, out var r, out var g, out var b);
        result[at++] = _Byte(r);
        result[at++] = _Byte(g);
        result[at++] = _Byte(b);
      }

    return result;
  }

  private static byte[] _Gray(H265Picture picture, BpgFile bpg) {
    var result = new byte[checked(bpg.Width * bpg.Height)];
    var depth = bpg.BitDepth;
    var max = (1 << depth) - 1;
    var scale = 1 << (depth - 8);
    var black = bpg.LimitedRange ? 16 * scale : 0;
    var span = bpg.LimitedRange ? 219 * scale : max;

    var at = 0;
    for (var y = 0; y < bpg.Height; ++y)
      for (var x = 0; x < bpg.Width; ++x)
        result[at++] = _Byte((picture.Luma[y * picture.Width + x] - black) / (double)span);

    return result;
  }

  private static void _ToRgb(
    BpgColorSpace space,
    bool limitedRange,
    int y,
    int cb,
    int cr,
    int max,
    int lumaBlack,
    int lumaSpan,
    int chromaCentre,
    int chromaSpan,
    out double r,
    out double g,
    out double b
  ) {
    if (space == BpgColorSpace.Rgb) {
      g = (y - lumaBlack) / (double)lumaSpan;
      b = (cb - lumaBlack) / (double)lumaSpan;
      r = (cr - lumaBlack) / (double)lumaSpan;
      return;
    }

    if (space == BpgColorSpace.YCgCo) {
      var yy = y / (double)max;
      var cg = (cb - chromaCentre) / (double)max;
      var co = (cr - chromaCentre) / (double)max;
      r = yy - cg + co;
      g = yy + cg;
      b = yy - cg - co;

      // BPG limits YCgCo in RGB space, unlike YCbCr where Y and C are limited independently.
      if (limitedRange) {
        var black = lumaBlack / (double)max;
        var span = lumaSpan / (double)max;
        r = (r - black) / span;
        g = (g - black) / span;
        b = (b - black) / span;
      }
      return;
    }

    var yyNorm = (y - lumaBlack) / (double)lumaSpan;
    var cbNorm = (cb - chromaCentre) / (double)chromaSpan;
    var crNorm = (cr - chromaCentre) / (double)chromaSpan;
    var (kr, kb) = space switch {
      BpgColorSpace.YCbCrBT601 => (0.299, 0.114),
      BpgColorSpace.YCbCrBT709 => (0.2126, 0.0722),
      BpgColorSpace.YCbCrBT2020Ncl => (0.2627, 0.0593),
      _ => throw new NotSupportedException($"BPG color_space {(int)space} is not implemented."),
    };
    var kg = 1.0 - kr - kb;

    r = yyNorm + 2.0 * (1.0 - kr) * crNorm;
    g = yyNorm
        - 2.0 * kb * (1.0 - kb) / kg * cbNorm
        - 2.0 * kr * (1.0 - kr) / kg * crNorm;
    b = yyNorm + 2.0 * (1.0 - kb) * cbNorm;
  }

  /// <summary>Linearly interpolates chroma at the sample position BPG declares.</summary>
  private static int _Chroma(
    ushort[] plane,
    int stride,
    int x,
    int y,
    int shiftX,
    int shiftY,
    int displayedWidth,
    int displayedHeight,
    bool centeredHorizontally
  ) {
    var maxX = ((displayedWidth + (1 << shiftX) - 1) >> shiftX) - 1;
    var maxY = ((displayedHeight + (1 << shiftY) - 1) >> shiftY) - 1;
    var nearX = Math.Min(x >> shiftX, maxX);
    var nearY = Math.Min(y >> shiftY, maxY);

    int farX;
    int nearWeightX;
    int farWeightX;
    int horizontalShift;
    if (shiftX != 0 && centeredHorizontally) {
      farX = Math.Clamp((x & 1) == 0 ? nearX - 1 : nearX + 1, 0, maxX);
      nearWeightX = 3;
      farWeightX = 1;
      horizontalShift = 2;
    } else if (shiftX != 0 && (x & 1) != 0) {
      farX = Math.Min(nearX + 1, maxX);
      nearWeightX = farWeightX = 1;
      horizontalShift = 1;
    } else {
      farX = nearX;
      nearWeightX = 2;
      farWeightX = 0;
      horizontalShift = 1;
    }

    var top = nearWeightX * plane[nearY * stride + nearX] + farWeightX * plane[nearY * stride + farX];
    if (shiftY == 0)
      return (top + (1 << (horizontalShift - 1))) >> horizontalShift;

    var farY = Math.Clamp((y & 1) == 0 ? nearY - 1 : nearY + 1, 0, maxY);
    var bottom = nearWeightX * plane[farY * stride + nearX] + farWeightX * plane[farY * stride + farX];
    return (3 * top + bottom + (1 << (horizontalShift + 1))) >> (horizontalShift + 2);
  }

  private static byte _Byte(double value)
    => (byte)Math.Clamp((int)Math.Round(value * 255.0, MidpointRounding.AwayFromZero), 0, 255);
}
