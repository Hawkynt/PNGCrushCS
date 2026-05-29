using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using FileFormat.Core;

namespace Optimizer.Image;

/// <summary>Specifies the clockwise rotation angle.</summary>
public enum RotateAngle {
  /// <summary>90 degrees clockwise.</summary>
  CW90,
  /// <summary>180 degrees.</summary>
  CW180,
  /// <summary>270 degrees clockwise (= 90 degrees counter-clockwise).</summary>
  CW270,
}

/// <summary>Specifies the flip direction.</summary>
public enum FlipDirection {
  /// <summary>Mirror horizontally (left-right).</summary>
  Horizontal,
  /// <summary>Mirror vertically (top-bottom).</summary>
  Vertical,
}

/// <summary>Specifies where the source image is anchored when extending the canvas.</summary>
public enum AnchorPosition {
  /// <summary>Top-left corner.</summary>
  TopLeft,
  /// <summary>Top-center.</summary>
  TopCenter,
  /// <summary>Top-right corner.</summary>
  TopRight,
  /// <summary>Middle-left.</summary>
  MiddleLeft,
  /// <summary>Center.</summary>
  Center,
  /// <summary>Middle-right.</summary>
  MiddleRight,
  /// <summary>Bottom-left corner.</summary>
  BottomLeft,
  /// <summary>Bottom-center.</summary>
  BottomCenter,
  /// <summary>Bottom-right corner.</summary>
  BottomRight,
}

/// <summary>Specifies how an image is resized to fit the target dimensions.</summary>
public enum ResizeMode {
  /// <summary>Distort the image to exactly fill the target dimensions.</summary>
  Stretch,
  /// <summary>Preserve aspect ratio, letterbox remaining area with a solid color.</summary>
  Fit,
  /// <summary>Preserve aspect ratio, crop overflow so the target is fully covered.</summary>
  Fill,
  /// <summary>Extract an arbitrary rectangle from the source without scaling.</summary>
  CropRegion,
}

/// <summary>Specifies the interpolation algorithm used when scaling pixels.</summary>
public enum InterpolationHint {
  /// <summary>No interpolation — each output pixel takes the value of the nearest source pixel.</summary>
  NearestNeighbor,
  /// <summary>Bilinear interpolation — weighted average of the 2x2 nearest source pixels.</summary>
  Bilinear,
  /// <summary>Bicubic interpolation — weighted average of the 4x4 nearest source pixels.</summary>
  Bicubic,
}

/// <summary>Pure resize/crop/letterbox engine that operates on <see cref="RawImage"/>.</summary>
public static class ImageTransformer {

  /// <summary>
  /// Resizes a <see cref="RawImage"/> to the specified target dimensions using the given mode and interpolation.
  /// For <see cref="ResizeMode.CropRegion"/> use <see cref="Crop"/> instead.
  /// </summary>
  /// <param name="source">The source image to resize.</param>
  /// <param name="targetWidth">The desired output width in pixels. Must be greater than zero.</param>
  /// <param name="targetHeight">The desired output height in pixels. Must be greater than zero.</param>
  /// <param name="mode">How the image is fitted into the target dimensions.</param>
  /// <param name="hint">The interpolation algorithm to use. Defaults to <see cref="InterpolationHint.Bicubic"/>.</param>
  /// <param name="letterboxColor">Background color for letterboxed areas when using <see cref="ResizeMode.Fit"/>. Defaults to transparent black.</param>
  /// <returns>A new BGRA32 <see cref="RawImage"/> with the requested dimensions.</returns>
  public static RawImage Resize(RawImage source, int targetWidth, int targetHeight, ResizeMode mode, InterpolationHint hint = InterpolationHint.Bicubic, Color? letterboxColor = null) {
    ArgumentNullException.ThrowIfNull(source);
    if (targetWidth <= 0)
      throw new ArgumentOutOfRangeException(nameof(targetWidth), targetWidth, "Target width must be greater than zero.");
    if (targetHeight <= 0)
      throw new ArgumentOutOfRangeException(nameof(targetHeight), targetHeight, "Target height must be greater than zero.");

    if (mode == ResizeMode.CropRegion)
      throw new ArgumentException("Use Crop() for CropRegion mode.", nameof(mode));

    using var srcBmp = BitmapConverter.RawImageToBitmap(source);
    using var dstBmp = new Bitmap(targetWidth, targetHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(dstBmp);

    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.InterpolationMode = _MapInterpolation(hint);
    g.CompositingQuality = CompositingQuality.HighQuality;
    g.SmoothingMode = SmoothingMode.HighQuality;

    switch (mode) {
      case ResizeMode.Stretch:
        g.DrawImage(srcBmp, 0, 0, targetWidth, targetHeight);
        break;

      case ResizeMode.Fit: {
        var fill = letterboxColor ?? Color.FromArgb(0, 0, 0, 0);
        using var brush = new SolidBrush(fill);
        g.FillRectangle(brush, 0, 0, targetWidth, targetHeight);

        var scaleX = targetWidth / (float)source.Width;
        var scaleY = targetHeight / (float)source.Height;
        var scale = Math.Min(scaleX, scaleY);
        var fitW = (int)(source.Width * scale);
        var fitH = (int)(source.Height * scale);
        var offsetX = (targetWidth - fitW) / 2;
        var offsetY = (targetHeight - fitH) / 2;
        g.DrawImage(srcBmp, offsetX, offsetY, fitW, fitH);
        break;
      }

      case ResizeMode.Fill: {
        var scaleX = targetWidth / (float)source.Width;
        var scaleY = targetHeight / (float)source.Height;
        var scale = Math.Max(scaleX, scaleY);
        var fillW = (int)(source.Width * scale);
        var fillH = (int)(source.Height * scale);
        var offsetX = (targetWidth - fillW) / 2;
        var offsetY = (targetHeight - fillH) / 2;
        g.DrawImage(srcBmp, offsetX, offsetY, fillW, fillH);
        break;
      }
    }

    return BitmapConverter.BitmapToRawImage(dstBmp);
  }

  /// <summary>
  /// Crops a rectangular region from the source image without scaling.
  /// Operates on raw BGRA32 pixel data without GDI+ involvement.
  /// The region is clamped to source bounds so no out-of-range access occurs.
  /// </summary>
  /// <param name="source">The source image to crop.</param>
  /// <param name="region">The rectangle to extract. Clamped to source bounds.</param>
  /// <returns>A new BGRA32 <see cref="RawImage"/> containing only the cropped pixels.</returns>
  public static RawImage Crop(RawImage source, Rectangle region) {
    ArgumentNullException.ThrowIfNull(source);

    var bgra = source.ToBgra32();
    var srcWidth = source.Width;
    var srcHeight = source.Height;

    // Clamp region to source bounds
    var x = Math.Max(0, region.X);
    var y = Math.Max(0, region.Y);
    var right = Math.Min(srcWidth, region.X + region.Width);
    var bottom = Math.Min(srcHeight, region.Y + region.Height);
    var cropW = Math.Max(0, right - x);
    var cropH = Math.Max(0, bottom - y);

    if (cropW == 0 || cropH == 0)
      throw new ArgumentException("Crop region is empty after clamping to source bounds.", nameof(region));

    const int bpp = 4; // BGRA32
    var rowBytes = cropW * bpp;
    var srcStride = srcWidth * bpp;
    var dstData = new byte[cropW * cropH * bpp];

    for (var row = 0; row < cropH; ++row) {
      var srcOffset = (y + row) * srcStride + x * bpp;
      var dstOffset = row * rowBytes;
      Array.Copy(bgra, srcOffset, dstData, dstOffset, rowBytes);
    }

    return new RawImage {
      Width = cropW,
      Height = cropH,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = dstData,
    };
  }

  /// <summary>
  /// Guesses the best interpolation mode based on the source image characteristics.
  /// Returns <see cref="InterpolationHint.NearestNeighbor"/> for indexed or small (likely retro) images,
  /// <see cref="InterpolationHint.Bicubic"/> for everything else.
  /// </summary>
  /// <param name="source">The source image to analyze.</param>
  /// <returns>The recommended interpolation hint.</returns>
  public static InterpolationHint GuessInterpolation(RawImage source) {
    ArgumentNullException.ThrowIfNull(source);

    // Indexed formats are typically pixel-art or retro — nearest neighbor preserves sharp edges
    if (source.Format is FileFormat.Core.PixelFormat.Indexed1 or FileFormat.Core.PixelFormat.Indexed4 or FileFormat.Core.PixelFormat.Indexed8)
      return InterpolationHint.NearestNeighbor;

    // Small images are likely retro/pixel-art
    if (source.Width <= 320 || source.Height <= 240)
      return InterpolationHint.NearestNeighbor;

    return InterpolationHint.Bicubic;
  }

  /// <summary>
  /// Rotates a <see cref="RawImage"/> by the specified angle.
  /// Operates on raw BGRA32 pixel data without GDI+ involvement.
  /// </summary>
  /// <param name="source">The source image to rotate.</param>
  /// <param name="angle">The clockwise rotation angle.</param>
  /// <returns>A new BGRA32 <see cref="RawImage"/> with the rotated pixels.</returns>
  public static RawImage Rotate(RawImage source, RotateAngle angle) {
    ArgumentNullException.ThrowIfNull(source);

    var bgra = source.ToBgra32();
    var srcW = source.Width;
    var srcH = source.Height;
    const int bpp = 4;

    int dstW, dstH;
    switch (angle) {
      case RotateAngle.CW90:
      case RotateAngle.CW270:
        dstW = srcH;
        dstH = srcW;
        break;
      default: // CW180
        dstW = srcW;
        dstH = srcH;
        break;
    }

    var dstData = new byte[dstW * dstH * bpp];

    for (var y = 0; y < srcH; ++y) {
      for (var x = 0; x < srcW; ++x) {
        int dstX, dstY;
        switch (angle) {
          case RotateAngle.CW90:
            dstX = srcH - 1 - y;
            dstY = x;
            break;
          case RotateAngle.CW180:
            dstX = srcW - 1 - x;
            dstY = srcH - 1 - y;
            break;
          default: // CW270
            dstX = y;
            dstY = srcW - 1 - x;
            break;
        }

        var srcOffset = (y * srcW + x) * bpp;
        var dstOffset = (dstY * dstW + dstX) * bpp;
        dstData[dstOffset] = bgra[srcOffset];
        dstData[dstOffset + 1] = bgra[srcOffset + 1];
        dstData[dstOffset + 2] = bgra[srcOffset + 2];
        dstData[dstOffset + 3] = bgra[srcOffset + 3];
      }
    }

    return new RawImage {
      Width = dstW,
      Height = dstH,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = dstData,
    };
  }

  /// <summary>
  /// Flips a <see cref="RawImage"/> horizontally or vertically.
  /// Operates on raw BGRA32 pixel data without GDI+ involvement.
  /// </summary>
  /// <param name="source">The source image to flip.</param>
  /// <param name="direction">The flip direction.</param>
  /// <returns>A new BGRA32 <see cref="RawImage"/> with the flipped pixels.</returns>
  public static RawImage Flip(RawImage source, FlipDirection direction) {
    ArgumentNullException.ThrowIfNull(source);

    var bgra = source.ToBgra32();
    var w = source.Width;
    var h = source.Height;
    const int bpp = 4;
    var stride = w * bpp;
    var dstData = new byte[w * h * bpp];

    switch (direction) {
      case FlipDirection.Horizontal:
        for (var y = 0; y < h; ++y) {
          for (var x = 0; x < w; ++x) {
            var srcOffset = y * stride + x * bpp;
            var dstOffset = y * stride + (w - 1 - x) * bpp;
            dstData[dstOffset] = bgra[srcOffset];
            dstData[dstOffset + 1] = bgra[srcOffset + 1];
            dstData[dstOffset + 2] = bgra[srcOffset + 2];
            dstData[dstOffset + 3] = bgra[srcOffset + 3];
          }
        }
        break;

      case FlipDirection.Vertical:
        for (var y = 0; y < h; ++y) {
          var srcRowOffset = y * stride;
          var dstRowOffset = (h - 1 - y) * stride;
          Array.Copy(bgra, srcRowOffset, dstData, dstRowOffset, stride);
        }
        break;
    }

    return new RawImage {
      Width = w,
      Height = h,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = dstData,
    };
  }

  /// <summary>
  /// Extends (or pads) the canvas of a <see cref="RawImage"/> to the specified dimensions,
  /// placing the source at the given anchor position and filling the remainder with the specified color.
  /// Operates on raw BGRA32 pixel data without GDI+ involvement.
  /// </summary>
  /// <param name="source">The source image.</param>
  /// <param name="newWidth">The new canvas width. Must be at least the source width.</param>
  /// <param name="newHeight">The new canvas height. Must be at least the source height.</param>
  /// <param name="anchor">Where to place the source image on the new canvas.</param>
  /// <param name="fillColor">The color to fill the new canvas area with.</param>
  /// <returns>A new BGRA32 <see cref="RawImage"/> with the extended canvas.</returns>
  public static RawImage ExtendCanvas(RawImage source, int newWidth, int newHeight, AnchorPosition anchor, Color fillColor) {
    ArgumentNullException.ThrowIfNull(source);
    if (newWidth < 1)
      throw new ArgumentOutOfRangeException(nameof(newWidth), newWidth, "New width must be at least 1.");
    if (newHeight < 1)
      throw new ArgumentOutOfRangeException(nameof(newHeight), newHeight, "New height must be at least 1.");

    var bgra = source.ToBgra32();
    var srcW = source.Width;
    var srcH = source.Height;
    const int bpp = 4;

    var dstData = new byte[newWidth * newHeight * bpp];

    // Fill with fill color
    byte b = fillColor.B, g = fillColor.G, r = fillColor.R, a = fillColor.A;
    for (var i = 0; i < dstData.Length; i += bpp) {
      dstData[i] = b;
      dstData[i + 1] = g;
      dstData[i + 2] = r;
      dstData[i + 3] = a;
    }

    // Compute source placement offset from anchor
    var (offsetX, offsetY) = _ComputeAnchorOffset(srcW, srcH, newWidth, newHeight, anchor);

    // Copy source rows into the target
    var srcStride = srcW * bpp;
    var dstStride = newWidth * bpp;
    var copyW = Math.Min(srcW, newWidth - Math.Max(0, offsetX));
    var copyH = Math.Min(srcH, newHeight - Math.Max(0, offsetY));

    for (var row = 0; row < copyH; ++row) {
      var srcRow = row;
      var dstRow = offsetY + row;
      if (dstRow < 0 || dstRow >= newHeight) continue;

      var srcX = 0;
      var dstX = offsetX;
      if (dstX < 0) { srcX = -dstX; dstX = 0; }
      var actualCopyW = Math.Min(copyW, newWidth - dstX);
      if (srcX >= srcW || actualCopyW <= 0) continue;
      actualCopyW = Math.Min(actualCopyW, srcW - srcX);

      var srcOffset = srcRow * srcStride + srcX * bpp;
      var dstOffset = dstRow * dstStride + dstX * bpp;
      Array.Copy(bgra, srcOffset, dstData, dstOffset, actualCopyW * bpp);
    }

    return new RawImage {
      Width = newWidth,
      Height = newHeight,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = dstData,
    };
  }

  internal static (int offsetX, int offsetY) _ComputeAnchorOffset(int srcW, int srcH, int dstW, int dstH, AnchorPosition anchor) => anchor switch {
    AnchorPosition.TopLeft => (0, 0),
    AnchorPosition.TopCenter => ((dstW - srcW) / 2, 0),
    AnchorPosition.TopRight => (dstW - srcW, 0),
    AnchorPosition.MiddleLeft => (0, (dstH - srcH) / 2),
    AnchorPosition.Center => ((dstW - srcW) / 2, (dstH - srcH) / 2),
    AnchorPosition.MiddleRight => (dstW - srcW, (dstH - srcH) / 2),
    AnchorPosition.BottomLeft => (0, dstH - srcH),
    AnchorPosition.BottomCenter => ((dstW - srcW) / 2, dstH - srcH),
    AnchorPosition.BottomRight => (dstW - srcW, dstH - srcH),
    _ => ((dstW - srcW) / 2, (dstH - srcH) / 2),
  };

  private static InterpolationMode _MapInterpolation(InterpolationHint hint) => hint switch {
    InterpolationHint.NearestNeighbor => InterpolationMode.NearestNeighbor,
    InterpolationHint.Bilinear => InterpolationMode.HighQualityBilinear,
    InterpolationHint.Bicubic => InterpolationMode.HighQualityBicubic,
    _ => InterpolationMode.HighQualityBicubic,
  };
}
