using System;
using System.Collections.Generic;
using System.Drawing;
using Hawkynt.GifFileFormat;

namespace Optimizer.Gif;

internal static class GifFrameOptimizer {
  /// <summary>
  ///   Merges identical consecutive frames by summing their delays.
  ///   Returns a new GifFile with duplicates removed, or the original if no duplicates found.
  /// </summary>
  public static GifFile DeduplicateFrames(GifFile gif) {
    if (gif.Frames.Count <= 1)
      return gif;

    var mergedFrames = new List<Frame>();
    var current = gif.Frames[0];
    var accumulatedDelay = current.Delay;

    for (var i = 1; i < gif.Frames.Count; ++i) {
      var next = gif.Frames[i];

      if (_FramesAreIdentical(current, next, gif.GlobalColorTable)) {
        accumulatedDelay = accumulatedDelay.Add(next.Delay);
        continue;
      }

      mergedFrames.Add(accumulatedDelay != current.Delay ? current.WithDelay(accumulatedDelay) : current);
      current = next;
      accumulatedDelay = next.Delay;
    }

    mergedFrames.Add(accumulatedDelay != current.Delay ? current.WithDelay(accumulatedDelay) : current);

    if (mergedFrames.Count == gif.Frames.Count)
      return gif;

    return new GifFile(gif.Version, gif.LogicalScreenSize, gif.GlobalColorTable, gif.LoopCount,
      gif.BackgroundColorIndex, mergedFrames);
  }

  private static bool _FramesAreIdentical(Frame a, Frame b, Color[]? globalColorTable) {
    if (a.Position.X != b.Position.X || a.Position.Y != b.Position.Y)
      return false;
    if (a.Size.Width != b.Size.Width || a.Size.Height != b.Size.Height)
      return false;

    var effectiveA = a.LocalColorTable ?? globalColorTable;
    var effectiveB = b.LocalColorTable ?? globalColorTable;

    // Both must have a resolvable palette
    if (effectiveA == null || effectiveB == null)
      return effectiveA == null && effectiveB == null && a.IndexedPixels.AsSpan().SequenceEqual(b.IndexedPixels);

    // Fast path: same effective palette instance or identical contents → compare raw indices
    var samePalette = ReferenceEquals(effectiveA, effectiveB);
    if (!samePalette && effectiveA.Length == effectiveB.Length) {
      samePalette = true;
      for (var i = 0; i < effectiveA.Length; ++i)
        if (effectiveA[i].ToArgb() != effectiveB[i].ToArgb()) {
          samePalette = false;
          break;
        }
    }

    if (samePalette) {
      if (a.TransparentColorIndex != b.TransparentColorIndex)
        return false;

      return a.IndexedPixels.AsSpan().SequenceEqual(b.IndexedPixels);
    }

    // Slow path: different palettes → compare resolved RGB per pixel
    var pixelsA = a.IndexedPixels;
    var pixelsB = b.IndexedPixels;
    if (pixelsA.Length != pixelsB.Length)
      return false;

    var tiA = a.TransparentColorIndex;
    var tiB = b.TransparentColorIndex;

    for (var i = 0; i < pixelsA.Length; ++i) {
      var idxA = pixelsA[i];
      var idxB = pixelsB[i];
      var isTransparentA = tiA.HasValue && idxA == tiA.Value;
      var isTransparentB = tiB.HasValue && idxB == tiB.Value;

      if (isTransparentA != isTransparentB)
        return false;
      if (isTransparentA)
        continue;

      if (idxA >= effectiveA.Length || idxB >= effectiveB.Length)
        return false;

      var colorA = effectiveA[idxA];
      var colorB = effectiveB[idxB];
      if (colorA.R != colorB.R || colorA.G != colorB.G || colorA.B != colorB.B)
        return false;
    }

    return true;
  }

  /// <summary>
  ///   Compression-aware disposal optimization: for each frame, try all 3 disposal methods
  ///   and pick the one that minimizes the NEXT frame's compressed diff size.
  /// </summary>
  public static FrameDisposalMethod[] OptimizeDisposalMethodsByCompression(GifFile gif) {
    var frameCount = gif.Frames.Count;
    if (frameCount <= 1)
      return frameCount == 0 ? [] : [FrameDisposalMethod.Unspecified];

    var disposals = new FrameDisposalMethod[frameCount];
    var screenW = gif.LogicalScreenSize.Width;
    var screenH = gif.LogicalScreenSize.Height;
    var canvas = new byte[screenW * screenH];
    var canvasValid = new bool[screenW * screenH];

    var disposalCandidates = new[] {
      FrameDisposalMethod.DoNotDispose,
      FrameDisposalMethod.RestoreToBackground,
      FrameDisposalMethod.RestoreToPrevious
    };

    // For RestoreToPrevious, keep a snapshot stack
    var previousCanvas = new byte[screenW * screenH];
    var previousCanvasValid = new bool[screenW * screenH];

    for (var i = 0; i < frameCount; ++i) {
      var frame = gif.Frames[i];
      var palette = frame.LocalColorTable ?? gif.GlobalColorTable;

      if (i == frameCount - 1 || palette == null) {
        disposals[i] = FrameDisposalMethod.Unspecified;
        _CompositeFrame(canvas, canvasValid, frame, palette, screenW, screenH);
        continue;
      }

      var nextFrame = gif.Frames[i + 1];
      var nextPalette = nextFrame.LocalColorTable ?? gif.GlobalColorTable;

      if (nextPalette == null) {
        disposals[i] = FrameDisposalMethod.DoNotDispose;
        _CompositeFrame(canvas, canvasValid, frame, palette, screenW, screenH);
        continue;
      }

      // Save canvas state before compositing this frame
      Array.Copy(canvas, previousCanvas, canvas.Length);
      Array.Copy(canvasValid, previousCanvasValid, canvasValid.Length);

      // Composite current frame onto canvas
      _CompositeFrame(canvas, canvasValid, frame, palette, screenW, screenH);

      var bestDisposal = FrameDisposalMethod.DoNotDispose;
      var bestSize = int.MaxValue;

      foreach (var disposal in disposalCandidates) {
        // Simulate canvas state after applying this disposal
        var simCanvas = (byte[])canvas.Clone();
        var simValid = (bool[])canvasValid.Clone();
        _SimulateDisposal(simCanvas, simValid, frame, previousCanvas, previousCanvasValid, disposal, screenW,
          screenH);

        // Compute diff size for next frame against this simulated canvas
        var diffSize =
          _ComputeDiffCompressedSize(simCanvas, simValid, nextFrame, nextPalette, screenW, screenH);
        if (diffSize >= bestSize)
          continue;

        bestSize = diffSize;
        bestDisposal = disposal;
      }

      disposals[i] = bestDisposal;

      // Apply the chosen disposal to the actual canvas
      _SimulateDisposal(canvas, canvasValid, frame, previousCanvas, previousCanvasValid, bestDisposal, screenW,
        screenH);
    }

    return disposals;
  }

  private static void _CompositeFrame(byte[] canvas, bool[] canvasValid, Frame frame, Color[]? palette,
    int screenW, int screenH) {
    if (palette == null)
      return;

    var fw = frame.Size.Width;
    var fh = frame.Size.Height;
    var fx = frame.Position.X;
    var fy = frame.Position.Y;
    var ti = frame.TransparentColorIndex;

    for (var row = 0; row < fh; ++row)
    for (var col = 0; col < fw; ++col) {
      var cx = fx + col;
      var cy = fy + row;
      if (cx >= screenW || cy >= screenH)
        continue;

      var pixelIdx = frame.IndexedPixels[row * fw + col];
      if (ti.HasValue && pixelIdx == ti.Value)
        continue;

      var idx = cy * screenW + cx;
      canvas[idx] = pixelIdx;
      canvasValid[idx] = true;
    }
  }

  private static void _SimulateDisposal(byte[] canvas, bool[] canvasValid, Frame frame, byte[] prevCanvas,
    bool[] prevValid, FrameDisposalMethod disposal, int screenW, int screenH) {
    switch (disposal) {
      case FrameDisposalMethod.RestoreToBackground: {
        var fx = frame.Position.X;
        var fy = frame.Position.Y;
        var fw = frame.Size.Width;
        var fh = frame.Size.Height;
        for (var row = 0; row < fh; ++row)
        for (var col = 0; col < fw; ++col) {
          var cx = fx + col;
          var cy = fy + row;
          if (cx >= screenW || cy >= screenH)
            continue;

          var idx = cy * screenW + cx;
          canvas[idx] = 0;
          canvasValid[idx] = false;
        }

        break;
      }
      case FrameDisposalMethod.RestoreToPrevious:
        Array.Copy(prevCanvas, canvas, canvas.Length);
        Array.Copy(prevValid, canvasValid, canvasValid.Length);
        break;
      // DoNotDispose/Unspecified: canvas stays as-is
    }
  }

  private static int _ComputeDiffCompressedSize(byte[] canvas, bool[] canvasValid, Frame nextFrame,
    Color[] nextPalette, int screenW, int screenH) {
    var fw = nextFrame.Size.Width;
    var fh = nextFrame.Size.Height;
    var fx = nextFrame.Position.X;
    var fy = nextFrame.Position.Y;
    var pixels = nextFrame.IndexedPixels;
    var ti = nextFrame.TransparentColorIndex;

    // Count how many pixels match the canvas (would become transparent with frame differencing)
    var transparentCount = 0;
    for (var row = 0; row < fh; ++row)
    for (var col = 0; col < fw; ++col) {
      var cx = fx + col;
      var cy = fy + row;
      if (cx >= screenW || cy >= screenH)
        continue;

      var frameIdx = row * fw + col;
      var canvasIdx = cy * screenW + cx;
      var srcIdx = pixels[frameIdx];

      if (!canvasValid[canvasIdx] || srcIdx >= nextPalette.Length)
        continue;

      var srcColor = nextPalette[srcIdx];
      var canvasColorIdx = canvas[canvasIdx];
      if (canvasColorIdx >= nextPalette.Length)
        continue;

      var canvasColor = nextPalette[canvasColorIdx];
      if (srcColor.R == canvasColor.R && srcColor.G == canvasColor.G && srcColor.B == canvasColor.B)
        ++transparentCount;
    }

    // More transparent pixels = better compression (simpler LZW)
    // Use pixel count minus transparent as a proxy for compressed size
    return pixels.Length - transparentCount;
  }

  /// <summary>
  ///   Trims transparent margins from a frame, returning the cropped pixel data and updated position/size.
  /// </summary>
  public static (byte[] pixels, Offset position, Dimensions size) TrimTransparentMargins(
    byte[] pixels,
    Dimensions size,
    Offset position,
    byte? transparentIndex
  ) {
    if (transparentIndex == null || size.Width == 0 || size.Height == 0)
      return (pixels, position, size);

    var ti = transparentIndex.Value;
    int w = size.Width;
    int h = size.Height;

    // Find bounding box of non-transparent pixels
    var top = h;
    var bottom = 0;
    var left = w;
    var right = 0;

    for (var y = 0; y < h; ++y)
    for (var x = 0; x < w; ++x)
      if (pixels[y * w + x] != ti) {
        if (y < top) top = y;
        if (y > bottom) bottom = y;
        if (x < left) left = x;
        if (x > right) right = x;
      }

    if (bottom < top)
      // Entire frame is transparent; keep 1x1 pixel
      return ([ti], new Offset(position.X, position.Y), new Dimensions(1, 1));

    var newW = right - left + 1;
    var newH = bottom - top + 1;

    if (newW == w && newH == h)
      return (pixels, position, size);

    var trimmed = new byte[newW * newH];
    for (var y = 0; y < newH; ++y)
      Array.Copy(pixels, (top + y) * w + left, trimmed, y * newW, newW);

    return (
      trimmed,
      new Offset((ushort)(position.X + left), (ushort)(position.Y + top)),
      new Dimensions(newW, newH)
    );
  }

  /// <summary>
  ///   Determines the optimal disposal method for frames by analyzing inter-frame differences.
  /// </summary>
  public static FrameDisposalMethod[] OptimizeDisposalMethods(GifFile gif) {
    var frameCount = gif.Frames.Count;
    if (frameCount == 0)
      return [];

    var disposals = new FrameDisposalMethod[frameCount];

    if (frameCount == 1) {
      disposals[0] = FrameDisposalMethod.Unspecified;
      return disposals;
    }

    // For multi-frame GIFs, use RestoreToBackground when next frame covers different area
    for (var i = 0; i < frameCount; ++i) {
      if (i == frameCount - 1) {
        disposals[i] = FrameDisposalMethod.Unspecified;
        continue;
      }

      var current = gif.Frames[i];
      var next = gif.Frames[i + 1];

      // If next frame covers the entire canvas, current can use DoNotDispose
      if (next.Position is { X: 0, Y: 0 }
          && next.Size.Width == gif.LogicalScreenSize.Width
          && next.Size.Height == gif.LogicalScreenSize.Height)
        disposals[i] = FrameDisposalMethod.DoNotDispose;
      else
        disposals[i] = FrameDisposalMethod.RestoreToBackground;
    }

    return disposals;
  }

  /// <summary>
  ///   Determines whether a global color table can be used for all frames.
  ///   Returns null if frames use incompatible palettes.
  /// </summary>
  public static Color[]? TryBuildGlobalColorTable(GifFile gif) {
    if (gif.Frames.Count == 0)
      return gif.GlobalColorTable;

    // Collect all unique colors across all frame palettes
    var colorSet = new HashSet<int>();

    foreach (var frame in gif.Frames) {
      var palette = frame.LocalColorTable ?? gif.GlobalColorTable;
      if (palette == null)
        return null;

      foreach (var c in palette)
        colorSet.Add(c.ToArgb());
    }

    if (colorSet.Count > 256)
      return null;

    var result = new Color[colorSet.Count];
    var i = 0;
    foreach (var argb in colorSet)
      result[i++] = Color.FromArgb(argb);

    return result;
  }
}
