using System;
using FileFormat.Core.Vector;

namespace FileFormat.Pdf.Rendering;

/// <summary>
/// An image placed square to the device axes, resampled once to exactly the pixels it covers.
/// </summary>
/// <remarks>
/// Most images on a page are only scaled, perhaps flipped or turned a quarter, and for those
/// poppler does not sample per pixel: it rounds the placement to whole pixels and rescales the
/// picture to that size, box-averaging where it shrinks, interpolating bilinearly where it grows by
/// less than four times and replicating beyond that. Doing the same here keeps an image's pixels
/// where the oracle puts them, and an image placed one to one comes through untouched — the edges
/// are rounded (so a picture covering 0 to 100 covers exactly a hundred pixels) rather than taken as
/// <c>floor(x1) + 1</c>, which is where poppler adds a column the image does not reach.
/// </remarks>
internal sealed class PdfScaledImage : PdfPaint {

  private readonly int _left, _top, _width, _height;
  private readonly byte[]? _rgb;
  private readonly byte[]? _alpha;
  private readonly PdfPaint? _stencilPaint;

  private PdfScaledImage(int left, int top, int width, int height, byte[]? rgb, byte[]? alpha, PdfPaint? stencilPaint) {
    this._left = left;
    this._top = top;
    this._width = width;
    this._height = height;
    this._rgb = rgb;
    this._alpha = alpha;
    this._stencilPaint = stencilPaint;
  }

  /// <summary>The device rectangle the image covers.</summary>
  public PixelBox Box => new(this._left, this._top, this._left + this._width, this._top + this._height);

  /// <summary>Builds the resampled image when the placement is square to the axes; null otherwise.</summary>
  public static PdfScaledImage? TryCreate(PdfRasterImage image, Matrix2D ctm, PdfPaint? stencilPaint, int surfaceWidth, int surfaceHeight) {
    const double eps = 1e-9;
    var straight = Math.Abs(ctm.B) < eps && Math.Abs(ctm.C) < eps && Math.Abs(ctm.A) > eps && Math.Abs(ctm.D) > eps;
    var turned = Math.Abs(ctm.A) < eps && Math.Abs(ctm.D) < eps && Math.Abs(ctm.B) > eps && Math.Abs(ctm.C) > eps;
    if (!straight && !turned)
      return null;

    var (x0, y0) = ctm.Apply(0, 0);
    var (x1, y1) = ctm.Apply(1, 1);
    var left = Math.Floor(Math.Min(x0, x1) + 0.5);
    var right = Math.Floor(Math.Max(x0, x1) + 0.5);
    var top = Math.Floor(Math.Min(y0, y1) + 0.5);
    var bottom = Math.Floor(Math.Max(y0, y1) + 0.5);
    if (right <= left)
      right = left + 1;
    if (bottom <= top)
      bottom = top + 1;

    // Far outside the surface, or absurdly large: let the general sampler deal with it.
    if (right - left > 4 * (double)surfaceWidth + 8 || bottom - top > 4 * (double)surfaceHeight + 8 || right < -1 || bottom < -1 || left > surfaceWidth + 1 || top > surfaceHeight + 1)
      return null;

    var deviceWidth = (int)(right - left);
    var deviceHeight = (int)(bottom - top);

    // The target size in the image's own orientation.
    var targetWidth = straight ? deviceWidth : deviceHeight;
    var targetHeight = straight ? deviceHeight : deviceWidth;

    byte[]? rgb = null;
    if (image.Rgb != null) {
      var bilinear = targetWidth >= image.Width && targetHeight >= image.Height && (targetWidth > image.Width || targetHeight > image.Height)
                     && targetWidth / image.Width < 4 && targetHeight / image.Height < 4;
      rgb = _Resample(image.Rgb, 3, image.Width, image.Height, targetWidth, targetHeight, bilinear);
    }

    byte[]? alpha = null;
    if (image.Alpha != null) {
      var bilinear = !image.IsStencil && targetWidth >= image.AlphaWidth && targetHeight >= image.AlphaHeight && (targetWidth > image.AlphaWidth || targetHeight > image.AlphaHeight)
                     && targetWidth / image.AlphaWidth < 4 && targetHeight / image.AlphaHeight < 4;
      alpha = _Resample(image.Alpha, 1, image.AlphaWidth, image.AlphaHeight, targetWidth, targetHeight, bilinear);
    }

    // Reorder to device orientation.
    var deviceRgb = rgb == null ? null : new byte[deviceWidth * deviceHeight * 3];
    var deviceAlpha = alpha == null ? null : new byte[deviceWidth * deviceHeight];
    for (var tj = 0; tj < targetHeight; ++tj)
      for (var ti = 0; ti < targetWidth; ++ti) {
        int dx, dy;
        if (straight) {
          dx = ctm.A > 0 ? ti : targetWidth - 1 - ti;
          // Image row 0 is v = 1, which lands at the top when D is negative.
          dy = ctm.D < 0 ? tj : targetHeight - 1 - tj;
        } else {
          dy = ctm.B > 0 ? ti : targetWidth - 1 - ti;
          dx = ctm.C > 0 ? targetHeight - 1 - tj : tj;
        }

        var to = dy * deviceWidth + dx;
        var from = tj * targetWidth + ti;
        if (deviceRgb != null) {
          deviceRgb[to * 3] = rgb![from * 3];
          deviceRgb[to * 3 + 1] = rgb[from * 3 + 1];
          deviceRgb[to * 3 + 2] = rgb[from * 3 + 2];
        }

        if (deviceAlpha != null)
          deviceAlpha[to] = alpha![from];
      }

    return new PdfScaledImage((int)left, (int)top, deviceWidth, deviceHeight, deviceRgb, deviceAlpha, stencilPaint);
  }

  /// <summary>The device rectangle as a path, for the rasteriser to cover exactly.</summary>
  public VectorPath Outline() {
    var path = new VectorPath();
    path.AddRectangle(this._left, this._top, this._width, this._height);
    return path;
  }

  public override void Fill(int y, int x0, int count, Span<float> rgb, Span<float> alpha) {
    if (this._stencilPaint != null)
      this._stencilPaint.Fill(y, x0, count, rgb, alpha);

    var row = y - this._top;
    for (var i = 0; i < count; ++i) {
      var col = x0 + i - this._left;
      if ((uint)row >= (uint)this._height || (uint)col >= (uint)this._width) {
        alpha[i] = 0;
        continue;
      }

      var at = row * this._width + col;
      if (this._rgb != null) {
        rgb[i * 3] = this._rgb[at * 3] * (1f / 255);
        rgb[i * 3 + 1] = this._rgb[at * 3 + 1] * (1f / 255);
        rgb[i * 3 + 2] = this._rgb[at * 3 + 2] * (1f / 255);
      } else if (this._stencilPaint == null)
        rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = 0;

      if (this._alpha != null)
        alpha[i] *= this._alpha[at] * (1f / 255);
    }
  }

  /// <summary>Rescales interleaved samples: box average down, bilinear or replication up, per axis.</summary>
  private static byte[] _Resample(byte[] source, int channels, int width, int height, int targetWidth, int targetHeight, bool bilinear) {
    if (width == targetWidth && height == targetHeight)
      return source;

    // Horizontal pass to targetWidth × height, then vertical to targetHeight.
    var horizontal = _Axis(source, channels, width, height, targetWidth, bilinear, true);
    return _Axis(horizontal, channels, targetWidth, height, targetHeight, bilinear, false);
  }

  private static byte[] _Axis(byte[] source, int channels, int width, int height, int target, bool bilinear, bool alongX) {
    var length = alongX ? width : height;
    if (length == target)
      return source;

    var outWidth = alongX ? target : width;
    var outHeight = alongX ? height : target;
    var output = new byte[outWidth * outHeight * channels];
    var lines = alongX ? height : width;

    for (var line = 0; line < lines; ++line)
      for (var i = 0; i < target; ++i) {
        for (var c = 0; c < channels; ++c) {
          int value;
          if (target < length) {
            // Box: the source samples from floor(i·n/t) to floor((i+1)·n/t).
            var start = (int)((long)i * length / target);
            var end = Math.Max(start + 1, (int)((long)(i + 1) * length / target));
            var sum = 0;
            for (var s = start; s < end; ++s)
              sum += _Get(source, channels, width, alongX, line, s, c);
            value = sum / (end - start);
          } else if (bilinear) {
            // poppler's interpolation: position i·n/t with no half-pixel offset, truncated.
            var pos = (double)i * length / target;
            var s0 = Math.Min((int)pos, length - 1);
            var s1 = Math.Min(s0 + 1, length - 1);
            var f = pos - s0;
            value = (int)(_Get(source, channels, width, alongX, line, s0, c) * (1 - f) + _Get(source, channels, width, alongX, line, s1, c) * f);
          } else
            value = _Get(source, channels, width, alongX, line, Math.Min(length - 1, (int)((long)i * length / target)), c);

          var at = alongX ? (line * outWidth + i) : (i * outWidth + line);
          output[at * channels + c] = (byte)value;
        }
      }

    return output;
  }

  private static int _Get(byte[] source, int channels, int width, bool alongX, int line, int s, int c)
    => alongX ? source[(line * width + s) * channels + c] : source[(s * width + line) * channels + c];
}
