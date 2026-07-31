using System;

namespace FileFormat.Core;

/// <summary>How a resample decides what lies between two source pixels.</summary>
public enum Resampling {

  /// <summary>Takes the nearest source pixel. Keeps hard edges, which is what pixel art wants.</summary>
  Nearest,

  /// <summary>Blends the four surrounding pixels. Cheap and soft.</summary>
  Bilinear,

  /// <summary>Fits a curve through sixteen pixels. Keeps more of an edge than bilinear does.</summary>
  Bicubic,
}

/// <summary>Scaling a picture without asking the platform to do it.</summary>
/// <remarks>
/// Resizing used to go through GDI+, which meant a whole graphics stack — and a Windows-only one —
/// was pulled in to do arithmetic on four bytes at a time. The three kernels below are the ones that
/// mattered, and doing them here means the same picture comes out on every machine rather than
/// whatever the local graphics library happens to do.
/// <para/>
/// Channels are interpolated as they are stored, alpha included and not premultiplied. That differs
/// from what GDI+ does internally, and it is the more predictable of the two: a fully transparent
/// pixel keeps its colour instead of dragging neighbours towards black.
/// </remarks>
public static class ImageResampler {

  /// <summary>Scales a picture to a given size.</summary>
  public static RawImage Resample(RawImage source, int width, int height, Resampling kind) {
    ArgumentNullException.ThrowIfNull(source);
    if (width < 1)
      throw new ArgumentOutOfRangeException(nameof(width), width, "A picture needs at least one pixel across.");
    if (height < 1)
      throw new ArgumentOutOfRangeException(nameof(height), height, "A picture needs at least one row.");

    var bgra = PixelConverter.Convert(source, PixelFormat.Bgra32);
    if (bgra.Width == width && bgra.Height == height)
      return bgra;

    var target = new byte[width * height * 4];
    var pixels = bgra.PixelData;
    int sourceWidth = bgra.Width, sourceHeight = bgra.Height;

    // Sample from pixel centres, so a scale of one lands exactly on the original grid rather than
    // half a pixel off it.
    var scaleX = (double)sourceWidth / width;
    var scaleY = (double)sourceHeight / height;

    for (var y = 0; y < height; ++y) {
      var sourceY = (y + 0.5) * scaleY - 0.5;

      for (var x = 0; x < width; ++x) {
        var sourceX = (x + 0.5) * scaleX - 0.5;
        var at = (y * width + x) * 4;

        switch (kind) {
          case Resampling.Nearest:
            _Nearest(pixels, sourceWidth, sourceHeight, sourceX, sourceY, target.AsSpan(at, 4));
            break;
          case Resampling.Bicubic:
            _Bicubic(pixels, sourceWidth, sourceHeight, sourceX, sourceY, target.AsSpan(at, 4));
            break;
          default:
            _Bilinear(pixels, sourceWidth, sourceHeight, sourceX, sourceY, target.AsSpan(at, 4));
            break;
        }
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = target };
  }

  private static void _Nearest(
    ReadOnlySpan<byte> pixels, int width, int height, double x, double y, Span<byte> target) {
    var at = (_Clamp((int)Math.Round(y), height) * width + _Clamp((int)Math.Round(x), width)) * 4;
    pixels.Slice(at, 4).CopyTo(target);
  }

  private static void _Bilinear(
    ReadOnlySpan<byte> pixels, int width, int height, double x, double y, Span<byte> target) {
    int left = (int)Math.Floor(x), top = (int)Math.Floor(y);
    double fx = x - left, fy = y - top;

    for (var channel = 0; channel < 4; ++channel) {
      var top1 = _At(pixels, width, height, left, top, channel) * (1 - fx)
                 + _At(pixels, width, height, left + 1, top, channel) * fx;
      var top2 = _At(pixels, width, height, left, top + 1, channel) * (1 - fx)
                 + _At(pixels, width, height, left + 1, top + 1, channel) * fx;

      target[channel] = _ToByte(top1 * (1 - fy) + top2 * fy);
    }
  }

  private static void _Bicubic(
    ReadOnlySpan<byte> pixels, int width, int height, double x, double y, Span<byte> target) {
    int left = (int)Math.Floor(x), top = (int)Math.Floor(y);
    double fx = x - left, fy = y - top;

    Span<double> column = stackalloc double[4];
    for (var channel = 0; channel < 4; ++channel) {
      for (var row = 0; row < 4; ++row) {
        Span<double> line = stackalloc double[4];
        for (var i = 0; i < 4; ++i)
          line[i] = _At(pixels, width, height, left - 1 + i, top - 1 + row, channel);

        column[row] = _CatmullRom(line, fx);
      }

      target[channel] = _ToByte(_CatmullRom(column, fy));
    }
  }

  /// <summary>A Catmull-Rom spline through four samples, evaluated between the middle two.</summary>
  private static double _CatmullRom(ReadOnlySpan<double> p, double t)
    => 0.5 * (2 * p[1]
              + (-p[0] + p[2]) * t
              + (2 * p[0] - 5 * p[1] + 4 * p[2] - p[3]) * t * t
              + (-p[0] + 3 * p[1] - 3 * p[2] + p[3]) * t * t * t);

  /// <summary>One channel of one pixel, with coordinates clamped to the edge rather than wrapped.</summary>
  private static double _At(ReadOnlySpan<byte> pixels, int width, int height, int x, int y, int channel)
    => pixels[(_Clamp(y, height) * width + _Clamp(x, width)) * 4 + channel];

  private static int _Clamp(int value, int length) => value < 0 ? 0 : value >= length ? length - 1 : value;

  /// <summary>A spline can overshoot past both ends of the range, so the result is clamped.</summary>
  private static byte _ToByte(double value) => value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)(value + 0.5);
}
