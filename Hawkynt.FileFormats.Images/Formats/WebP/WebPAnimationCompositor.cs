using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.WebP.Vp8;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP;

/// <summary>Plays an animation forward and hands back the canvas as it stood at a chosen frame.</summary>
/// <remarks>
/// <para>
/// Ported from libwebp's own animation decoder (<c>src/demux/anim_decode.c</c>), decision for
/// decision, because the answers that matter here are not derivable from the container
/// specification and the three decoders in circulation were measured to agree with libwebp rather
/// than with the specification's wording:
/// </para>
/// <list type="bullet">
///   <item>The canvas starts as, and disposal clears to, <em>transparent black</em>. The
///     specification says "background color", and a file can state one; painting it produces a
///     picture that looks deliberate and matches nothing. Checked against <c>anim_dump</c>, ffmpeg
///     and ImageMagick on a file whose ANIM chunk names opaque magenta: all three leave
///     (0, 0, 0, 0) behind.</item>
///   <item>Blending is integer arithmetic with a truncating reciprocal, not the rounded
///     floating-point "source over" it approximates. Half-transparent red over opaque blue is
///     (127, 0, 126) here and in ffmpeg; the float formula, and ImageMagick, say (128, 0, 127).</item>
///   <item>A frame that needs nothing from its predecessors is treated as a key frame and drawn
///     onto a cleared canvas without blending at all. This is not only an optimisation: for such a
///     frame libwebp skips the blend, and the blend's truncation means skipping it and performing
///     it against transparent black do not always give the same byte.</item>
/// </list>
/// <para>
/// Frames are composited from the beginning every time rather than cached, so asking for frame
/// <c>n</c> costs <c>n</c> decodes. Animations here are tens of frames, and a cache would have to be
/// invalidated against a mutable frame list; if that ever stops being true, the loop is the place to
/// hang one.
/// </para>
/// </remarks>
internal static class WebPAnimationCompositor {

  /// <summary>Composites frames 0..<paramref name="index"/> and returns the canvas as it stood when
  /// frame <paramref name="index"/> was shown, as RGBA bytes.</summary>
  public static byte[] Compose(WebPFile file, int index) {
    byte[]? answer = null;
    foreach (var canvas in _Play(file, index))
      answer = canvas;

    return answer ?? throw new InvalidOperationException("Frame index was not reached.");
  }

  /// <summary>Every frame's canvas, in playing order, composited in one pass.</summary>
  /// <remarks>
  /// Asking <see cref="Compose"/> for each frame in turn would replay the animation from the start
  /// every time, so reading all of a hundred-frame animation would cost five thousand frame decodes
  /// instead of a hundred. Callers that want the whole thing come through here.
  /// </remarks>
  public static IReadOnlyList<byte[]> ComposeAll(WebPFile file) {
    var all = new List<byte[]>(file.Frames.Count);
    foreach (var canvas in _Play(file, file.Frames.Count - 1))
      all.Add((byte[])canvas.Clone());

    return all;
  }

  /// <summary>Runs the animation up to and including <paramref name="index"/>, yielding the canvas
  /// after each frame. The yielded array is the live canvas and is overwritten by the next step.</summary>
  private static IEnumerable<byte[]> _Play(WebPFile file, int index) {
    var canvasWidth = file.Features.Width;
    var canvasHeight = file.Features.Height;
    var frames = file.Frames;

    var current = new byte[canvasWidth * canvasHeight * 4];
    var previousDisposed = new byte[current.Length];

    var previousWasKeyFrame = false;
    WebPFrame? previous = null;

    for (var i = 0; i <= index; ++i) {
      var frame = frames[i];
      _Validate(frame, canvasWidth, canvasHeight, i);

      var isKeyFrame = _IsKeyFrame(frame, previous, previousWasKeyFrame, i, canvasWidth, canvasHeight);
      if (isKeyFrame)
        Array.Clear(current);
      else
        Buffer.BlockCopy(previousDisposed, 0, current, 0, current.Length);

      var pixels = _DecodeFrame(frame);
      _WriteRect(current, canvasWidth, frame, pixels);

      // libwebp blends the freshly written rectangle against the canvas the previous frame left,
      // which is why the frame is written in whole first and reconciled afterwards rather than
      // blended pixel by pixel on the way in.
      if (i > 0 && frame.BlendMethod == WebPFrameBlendMethod.AlphaBlend && !isKeyFrame)
        _Blend(current, previousDisposed, canvasWidth, frame, previous);

      yield return current;

      Buffer.BlockCopy(current, 0, previousDisposed, 0, current.Length);
      if (frame.DisposalMethod == WebPFrameDisposalMethod.Background)
        _ClearRect(previousDisposed, canvasWidth, frame);

      previousWasKeyFrame = isKeyFrame;
      previous = frame;
    }
  }

  private static void _Validate(WebPFrame frame, int canvasWidth, int canvasHeight, int index) {
    if (frame.Width <= 0 || frame.Height <= 0)
      throw new InvalidDataException($"WebP animation frame {index} states an empty {frame.Width}x{frame.Height} rectangle.");
    if (frame.X < 0 || frame.Y < 0
        || frame.X + frame.Width > canvasWidth
        || frame.Y + frame.Height > canvasHeight)
      throw new InvalidDataException(
        $"WebP animation frame {index} states a {frame.Width}x{frame.Height} rectangle at ({frame.X}, {frame.Y}), which leaves the {canvasWidth}x{canvasHeight} canvas.");
  }

  /// <summary>Whether this frame owes nothing to the ones before it.</summary>
  /// <remarks>libwebp's <c>IsKeyFrame</c>, unchanged.</remarks>
  private static bool _IsKeyFrame(
    WebPFrame frame, WebPFrame? previous, bool previousWasKeyFrame, int index, int canvasWidth, int canvasHeight) {
    if (index == 0)
      return true;

    var isFullFrame = frame.Width == canvasWidth && frame.Height == canvasHeight;
    if ((!frame.HasAlpha || frame.BlendMethod == WebPFrameBlendMethod.None) && isFullFrame)
      return true;

    if (previous == null)
      return false;

    // Whatever the previous frame disposed is transparent now; if that was the whole canvas, or the
    // frame before it had already cleared everything it did not itself cover, there is nothing left
    // underneath this one.
    return previous.DisposalMethod == WebPFrameDisposalMethod.Background
           && (previous.Width == canvasWidth && previous.Height == canvasHeight || previousWasKeyFrame);
  }

  /// <summary>Decodes one frame's own rectangle into RGBA bytes.</summary>
  private static byte[] _DecodeFrame(WebPFrame frame) {
    var pixelCount = frame.Width * frame.Height;
    var rgba = new byte[pixelCount * 4];

    if (frame.IsLossless) {
      // The VP8L header states a size of its own, and a frame whose picture is not the size its
      // ANMF rectangle claims is a file nobody can render — the lossy path already refuses that, so
      // this one does too rather than decoding into a buffer the two disagree about.
      var stated = WebPReader._ParseVp8L(frame.ImageData);
      if (stated.Width != frame.Width || stated.Height != frame.Height)
        throw new InvalidDataException(
          $"WebP animation frame states a {frame.Width}x{frame.Height} rectangle but its VP8L picture is {stated.Width}x{stated.Height}.");

      // Read straight from ARGB rather than through the still-picture path: that one drops alpha
      // when the header's alpha bit is clear, and a frame's alpha decides how it meets the canvas
      // even when the encoder judged it not worth advertising.
      var argb = Vp8LDecoder.DecodeArgbStream(frame.ImageData, 5, frame.Width, frame.Height);
      for (var i = 0; i < pixelCount; ++i) {
        var pixel = argb[i];
        rgba[i * 4 + 0] = (byte)((pixel >> 16) & 0xFF);
        rgba[i * 4 + 1] = (byte)((pixel >> 8) & 0xFF);
        rgba[i * 4 + 2] = (byte)(pixel & 0xFF);
        rgba[i * 4 + 3] = (byte)((pixel >> 24) & 0xFF);
      }

      return rgba;
    }

    var rgb = Vp8Decoder.Decode(frame.ImageData, frame.Width, frame.Height);
    var alpha = frame.AlphaChunk == null ? null : WebPAlphaDecoder.Decode(frame.AlphaChunk, frame.Width, frame.Height);
    for (var i = 0; i < pixelCount; ++i) {
      rgba[i * 4 + 0] = rgb[i * 3 + 0];
      rgba[i * 4 + 1] = rgb[i * 3 + 1];
      rgba[i * 4 + 2] = rgb[i * 3 + 2];
      rgba[i * 4 + 3] = alpha?[i] ?? 0xFF;
    }

    return rgba;
  }

  private static void _WriteRect(byte[] canvas, int canvasWidth, WebPFrame frame, byte[] pixels) {
    var rowBytes = frame.Width * 4;
    for (var y = 0; y < frame.Height; ++y)
      Buffer.BlockCopy(pixels, y * rowBytes, canvas, ((frame.Y + y) * canvasWidth + frame.X) * 4, rowBytes);
  }

  private static void _ClearRect(byte[] canvas, int canvasWidth, WebPFrame frame) {
    var rowBytes = frame.Width * 4;
    for (var y = 0; y < frame.Height; ++y)
      Array.Clear(canvas, ((frame.Y + y) * canvasWidth + frame.X) * 4, rowBytes);
  }

  /// <summary>Reconciles this frame's rectangle with the canvas underneath it.</summary>
  /// <remarks>
  /// Where the previous frame disposed its own rectangle, the canvas underneath is known to be
  /// transparent, and libwebp declares blending against transparent a no-op and skips it. That is
  /// not merely a saving: blending against transparent is a no-op in arithmetic but not in this
  /// arithmetic, whose reciprocal truncates and takes a count off every channel. Skipping it is the
  /// difference between (120, 135, 68) and (119, 134, 67) on a real file, and it is why ffmpeg —
  /// which blends unconditionally — disagrees with libwebp by one there. libwebp's answer is the
  /// arithmetically correct one, so that is the one followed here.
  /// </remarks>
  private static void _Blend(byte[] canvas, byte[] under, int canvasWidth, WebPFrame frame, WebPFrame? previous) {
    if (previous is not { DisposalMethod: WebPFrameDisposalMethod.Background }) {
      for (var y = 0; y < frame.Height; ++y)
        _BlendRun(canvas, under, (frame.Y + y) * canvasWidth + frame.X, frame.Width);

      return;
    }

    for (var y = 0; y < frame.Height; ++y) {
      var canvasY = frame.Y + y;
      var (left1, width1, left2, width2) = _BlendRangeAtRow(frame, previous, canvasY);
      if (width1 > 0)
        _BlendRun(canvas, under, canvasY * canvasWidth + left1, width1);
      if (width2 > 0)
        _BlendRun(canvas, under, canvasY * canvasWidth + left2, width2);
    }
  }

  /// <summary>The parts of <paramref name="frame"/>'s row that lie outside
  /// <paramref name="previous"/>'s rectangle — at most one to its left and one to its right.</summary>
  /// <remarks>libwebp's <c>FindBlendRangeAtRow</c>.</remarks>
  private static (int Left1, int Width1, int Left2, int Width2) _BlendRangeAtRow(
    WebPFrame frame, WebPFrame previous, int canvasY) {
    var frameMaxX = frame.X + frame.Width;
    var previousMaxX = previous.X + previous.Width;
    var previousMaxY = previous.Y + previous.Height;

    if (canvasY < previous.Y || canvasY >= previousMaxY
        || frame.X >= previousMaxX || frameMaxX <= previous.X)
      return (frame.X, frame.Width, -1, 0);

    var left1 = -1;
    var width1 = 0;
    if (frame.X < previous.X) {
      left1 = frame.X;
      width1 = previous.X - frame.X;
    }

    var left2 = -1;
    var width2 = 0;
    if (frameMaxX > previousMaxX) {
      left2 = previousMaxX;
      width2 = frameMaxX - previousMaxX;
    }

    return (left1, width1, left2, width2);
  }

  private static void _BlendRun(byte[] canvas, byte[] under, int firstPixel, int count) {
    var at = firstPixel * 4;
    for (var i = 0; i < count; ++i, at += 4) {
      // A fully opaque source pixel is left exactly as it is. Not a shortcut: the blend below
      // would answer 254 where the source says 255, because its reciprocal truncates.
      if (canvas[at + 3] == 0xFF)
        continue;

      _BlendPixel(canvas, under, at);
    }
  }

  /// <summary>libwebp's <c>BlendPixelNonPremult</c>, arithmetic for arithmetic.</summary>
  private static void _BlendPixel(byte[] canvas, byte[] under, int at) {
    var sourceAlpha = canvas[at + 3];
    if (sourceAlpha == 0) {
      canvas[at + 0] = under[at + 0];
      canvas[at + 1] = under[at + 1];
      canvas[at + 2] = under[at + 2];
      canvas[at + 3] = under[at + 3];
      return;
    }

    var destinationFactorAlpha = (byte)(under[at + 3] * (256 - sourceAlpha) >> 8);
    var blendedAlpha = (byte)(sourceAlpha + destinationFactorAlpha);
    var scale = (uint)((1U << 24) / blendedAlpha);

    canvas[at + 0] = _BlendChannel(canvas[at + 0], sourceAlpha, under[at + 0], destinationFactorAlpha, scale);
    canvas[at + 1] = _BlendChannel(canvas[at + 1], sourceAlpha, under[at + 1], destinationFactorAlpha, scale);
    canvas[at + 2] = _BlendChannel(canvas[at + 2], sourceAlpha, under[at + 2], destinationFactorAlpha, scale);
    canvas[at + 3] = blendedAlpha;
  }

  private static byte _BlendChannel(byte source, byte sourceAlpha, byte destination, byte destinationAlpha, uint scale)
    => (byte)((uint)(source * sourceAlpha + destination * destinationAlpha) * scale >> 24);
}
