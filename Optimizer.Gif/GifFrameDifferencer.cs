using System;
using System.Drawing;
using Hawkynt.GifFileFormat;

namespace Optimizer.Gif;

/// <summary>
///   Computes pixel differences between consecutive GIF frames.
///   Unchanged pixels become transparent, dramatically improving LZW compression.
/// </summary>
internal static class GifFrameDifferencer {
  /// <summary>
  ///   Computes frame diffs for a multi-frame GIF.
  ///   Returns modified frames where unchanged pixels are set to the transparent index.
  ///   Frame 0 is returned unmodified.
  /// </summary>
  public static Frame[] ComputeDiffs(GifFile gif) {
    var frames = gif.Frames;
    if (frames.Count <= 1)
      return [.. frames];

    var screenW = gif.LogicalScreenSize.Width;
    var screenH = gif.LogicalScreenSize.Height;

    // Canvas tracks the composited state of the display
    var canvas = new byte[screenW * screenH];
    // Track which canvas positions have been drawn to
    var canvasValid = new bool[screenW * screenH];

    var result = new Frame[frames.Count];

    for (var i = 0; i < frames.Count; ++i) {
      var frame = frames[i];
      var palette = frame.LocalColorTable ?? gif.GlobalColorTable;

      if (i == 0 || palette == null) {
        // First frame or no palette: output as-is, composite onto canvas
        result[i] = frame;
        _CompositeOntoCanvas(canvas, canvasValid, frame, palette, screenW);
        _ApplyDisposal(canvas, canvasValid, frame, palette, screenW, screenH);
        continue;
      }

      // Find or reserve a transparent index
      var (transparentIdx, newPalette) = _EnsureTransparentIndex(palette, frame.TransparentColorIndex);

      var srcPixels = frame.IndexedPixels;
      var fw = frame.Size.Width;
      var fh = frame.Size.Height;
      var fx = frame.Position.X;
      var fy = frame.Position.Y;
      var diffPixels = new byte[srcPixels.Length];
      var hasDiff = false;

      for (var row = 0; row < fh; ++row)
      for (var col = 0; col < fw; ++col) {
        var canvasX = fx + col;
        var canvasY = fy + row;
        var frameIdx = row * fw + col;
        var canvasIdx = canvasY * screenW + canvasX;

        if (canvasX >= screenW || canvasY >= screenH) {
          diffPixels[frameIdx] = srcPixels[frameIdx];
          continue;
        }

        var srcColorIdx = srcPixels[frameIdx];

        // If canvas position is valid and the pixel matches what's on canvas, make transparent
        if (canvasValid[canvasIdx] && srcColorIdx < newPalette.Length) {
          var srcColor = newPalette[srcColorIdx];
          var canvasColorIdx = canvas[canvasIdx];
          if (canvasColorIdx < newPalette.Length) {
            var canvasColor = newPalette[canvasColorIdx];
            if (srcColor.R == canvasColor.R && srcColor.G == canvasColor.G && srcColor.B == canvasColor.B) {
              diffPixels[frameIdx] = transparentIdx;
              hasDiff = true;
              continue;
            }
          }
        }

        diffPixels[frameIdx] = srcColorIdx;
      }

      if (hasDiff)
        result[i] = new Frame(
          diffPixels,
          frame.Size,
          frame.Position,
          newPalette != palette ? newPalette : frame.LocalColorTable,
          frame.Delay,
          frame.DisposalMethod,
          transparentIdx,
          frame.IsInterlaced
        );
      else
        result[i] = frame;

      // Composite original (non-diffed) pixels onto canvas for next frame's reference
      _CompositeOntoCanvas(canvas, canvasValid, frame, palette, screenW);
      _ApplyDisposal(canvas, canvasValid, frame, palette, screenW, screenH);
    }

    return result;
  }

  /// <summary>Ensure a transparent index exists in the palette.</summary>
  internal static (byte transparentIdx, Color[] palette) _EnsureTransparentIndex(Color[] palette,
    byte? existingTransparentIdx) {
    if (existingTransparentIdx.HasValue)
      return (existingTransparentIdx.Value, palette);

    // Try to find an unused slot or add one
    if (palette.Length < 256) {
      var newPalette = new Color[palette.Length + 1];
      Array.Copy(palette, newPalette, palette.Length);
      newPalette[palette.Length] = Color.FromArgb(0, 0, 0, 0);
      return ((byte)palette.Length, newPalette);
    }

    // Palette is full (256 colors); use the last index as transparent
    // This may lose a color but is rare for full palettes in practice
    return (255, palette);
  }

  private static void _CompositeOntoCanvas(byte[] canvas, bool[] canvasValid, Frame frame, Color[]? palette,
    int screenW) {
    if (palette == null)
      return;

    var pixels = frame.IndexedPixels;
    var fw = frame.Size.Width;
    var fh = frame.Size.Height;
    var fx = frame.Position.X;
    var fy = frame.Position.Y;
    var ti = frame.TransparentColorIndex;

    for (var row = 0; row < fh; ++row)
    for (var col = 0; col < fw; ++col) {
      var canvasX = fx + col;
      var canvasY = fy + row;
      if (canvasX >= screenW || canvasY >= canvas.Length / screenW)
        continue;

      var pixelIdx = pixels[row * fw + col];

      // Skip transparent pixels — they don't affect the canvas
      if (ti.HasValue && pixelIdx == ti.Value)
        continue;

      var canvasIdx = canvasY * screenW + canvasX;
      canvas[canvasIdx] = pixelIdx;
      canvasValid[canvasIdx] = true;
    }
  }

  private static void _ApplyDisposal(byte[] canvas, bool[] canvasValid, Frame frame, Color[]? palette, int screenW,
    int screenH) {
    switch (frame.DisposalMethod) {
      case FrameDisposalMethod.RestoreToBackground: {
        var fx = frame.Position.X;
        var fy = frame.Position.Y;
        var fw = frame.Size.Width;
        var fh = frame.Size.Height;
        for (var row = 0; row < fh; ++row)
        for (var col = 0; col < fw; ++col) {
          var cx = fx + col;
          var cy = fy + row;
          if (cx < screenW && cy < screenH) {
            var idx = cy * screenW + cx;
            canvas[idx] = 0;
            canvasValid[idx] = false;
          }
        }

        break;
      }
      // DoNotDispose and Unspecified: canvas stays as-is
      // RestoreToPrevious: would need canvas snapshot stack — skip for now, treat as DoNotDispose
    }
  }
}
