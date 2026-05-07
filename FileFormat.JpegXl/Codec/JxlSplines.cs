using System;

namespace FileFormat.JpegXl.Codec;

// =====================================================================================
// Splines (ISO/IEC 18181-1 §G.11; libjxl `lib/jxl/splines.cc`,
// `lib/jxl/splines.h`).
//
// Splines are a perceptual-quality refinement layer applied AFTER VarDCT
// reconstruction (between the IDCT and the patches/EPF/Gaborish loop in the
// frame pipeline). They model thin line-like features as parametric curves
// (centripetal Catmull-Rom interpolation between control points) with XYB
// color and Gaussian sigma values carried along the path as 32-coefficient
// DCTs. At apply-time, libjxl tessellates each curve into roughly
// 1-pixel-spaced "render points" and splats a 2D Gaussian per render point.
//
// The bitstream layout (libjxl `Splines::Decode` in splines.cc):
//   1. 1-bit HasSplines flag. If 0, splines are disabled — return null.
//   2. ANS histograms over kNumSplineContexts=6 contexts, plus context_map.
//   3. num_splines = ReadHybridUint(kNumSplinesContext) + 1.
//   4. For each spline: starting_point as (dx, dy) hybrid uints — first is
//      absolute, subsequent are deltas from the previous start.
//   5. quantization_adjustment = UnpackSigned(ReadHybridUint(...)).
//   6. For each spline: num_control_points + (delta_x_delta, delta_y_delta)
//      double-delta-encoded control-point deltas + 32 coefficients per
//      channel (X, Y, B) + 32 sigma coefficients, all signed-unpacked
//      hybrid uints.
//   7. Final ANS state check.
//
// First-wave implementation policy:
//   * `ReadList` reads the HasSplines flag. If 0, returns null.
//   * If 1, throws `NotImplementedException` with a precise message — the
//     full ANS-histogram-decoded spline list (including dequantization with
//     Y-to-X / Y-to-B color correlation, Catmull-Rom tessellation, and the
//     2D Gaussian splatting from `Splines::InitializeDrawCache` and
//     `Splines::AddTo`) is too involved to land in one wave. The current
//     wave correctly handles the overwhelmingly common case where splines
//     are disabled in the frame.
//   * `Apply` is a no-op for empty / null spline lists, which is also the
//     expected state for the first-wave VarDCT pipeline that doesn't yet
//     emit splines from the bitstream.
//
// Constants below (kNumSplineContexts=6, channel weights, kQuantization
// formulas, etc.) are kept verbatim from libjxl so the future full-decode
// implementation can be wired in without touching the public API.
//
// libjxl source links:
//   https://github.com/libjxl/libjxl/blob/main/lib/jxl/splines.h
//   https://github.com/libjxl/libjxl/blob/main/lib/jxl/splines.cc
// =====================================================================================

/// <summary>
/// Decoder for the JPEG XL splines layer (ISO/IEC 18181-1 §G.11).
/// </summary>
internal static class JxlSplines {

  /// <summary>Number of entropy contexts used by the spline decoder. Mirrors
  /// libjxl's <c>SplineEntropyContexts::kNumSplineContexts</c> in
  /// <c>splines.h</c>.</summary>
  internal const int NumSplineContexts = 6;

  /// <summary>libjxl <c>kDesiredRenderingDistance</c> (splines.h). Render
  /// points along a tessellated curve are spaced this many pixels apart.
  /// </summary>
  internal const float DesiredRenderingDistance = 1.0f;

  /// <summary>Per-channel spline weights. X, Y, B, sigma — taken verbatim from
  /// libjxl <c>kChannelWeight</c> in splines.cc.</summary>
  internal static readonly float[] ChannelWeights = [0.0042f, 0.075f, 0.07f, 0.3333f];

  /// <summary>
  /// Read the spline list from the bitstream. Returns <c>null</c> when the
  /// HasSplines flag is 0 (the common case when splines are disabled for the
  /// frame). When the flag is 1, the full spline list parse is not yet
  /// implemented and a <see cref="NotImplementedException"/> is thrown.
  /// </summary>
  /// <param name="reader">Bit reader positioned at the HasSplines flag.</param>
  /// <param name="entropy">Per-frame entropy decoder. Currently unused — when
  /// the full parse lands, the spline layer will allocate its own ANS
  /// distribution over <see cref="NumSplineContexts"/>.</param>
  public static SplineList? ReadList(JxlBitReader reader, JxlEntropyDecoder entropy) {
    ArgumentNullException.ThrowIfNull(reader);
    // entropy may be unused in this first-wave implementation; we keep the
    // parameter so that the public signature is stable when the full decoder
    // is wired in.
    _ = entropy;

    if (!ReadHasSplinesFlag(reader))
      return null;

    // ----- Full spline list parse not yet implemented. -----
    //
    // libjxl `Splines::Decode` (splines.cc) does:
    //   1. DecodeHistograms(br, kNumSplineContexts, &code, &context_map)
    //   2. ANSSymbolReader::Create(&code, br)
    //   3. num_splines = decoder.ReadHybridUint(kNumSplinesContext, br, ctx) + 1
    //   4. DecodeAllStartingPoints (delta-encoded {dx, dy} pairs)
    //   5. quantization_adjustment = UnpackSigned(...)
    //   6. Per-spline QuantizedSpline::Decode:
    //        * num_control_points = ReadHybridUint(kNumControlPointsContext, ...)
    //        * 2 * num_control_points double-delta-encoded signed values
    //        * 3 * 32 + 32 = 128 signed DCT coefficients
    //   7. decoder.CheckANSFinalState()
    //
    // Rendering (Splines::InitializeDrawCache + AddTo in splines.cc) requires
    // dequantization (with Y-to-X and Y-to-B color correlation factors that
    // are derived from the chroma-from-luma layer), centripetal Catmull-Rom
    // tessellation at kDesiredRenderingDistance spacing, then 2D Gaussian
    // splatting (using the FastErf approximation from base/fast_math-inl.h)
    // per render point per channel.
    //
    // None of those subsystems exist yet in this codec. Rather than emit a
    // silently-wrong spline list, we fail loudly so any test bitstream that
    // does enable splines surfaces immediately.
    throw new NotImplementedException(
      "Spline list decoding is not yet implemented. The HasSplines flag was 1 " +
      "in the bitstream, which requires: ANS histogram decoding over " +
      "kNumSplineContexts=6 contexts (libjxl `DecodeHistograms`); per-spline " +
      "starting-point deltas, control-point double-delta decoding, and " +
      "3*32+32 quantized DCT coefficients (libjxl `QuantizedSpline::Decode`); " +
      "dequantization with Y-to-X / Y-to-B color correlation factors and " +
      "manhattan-distance area-limit checks (libjxl `QuantizedSpline::Dequantize`); " +
      "centripetal Catmull-Rom tessellation at kDesiredRenderingDistance=1px " +
      "spacing (libjxl `DrawCentripetalCatmullRomSpline`); and 2D Gaussian " +
      "splatting via the FastErf approximation (libjxl `DrawSegment`). See " +
      "lib/jxl/splines.cc for the reference implementation. " +
      "Tracked as a follow-up to the VarDCT first-wave decoder.");
  }

  /// <summary>
  /// Consume the 1-bit <c>HasSplines</c> flag from the bitstream and return
  /// it without throwing on either value. This is the structural seam
  /// callers can use to advance the bit position even when the full list
  /// parse is unavailable.
  /// </summary>
  public static bool ReadHasSplinesFlag(JxlBitReader reader) {
    ArgumentNullException.ThrowIfNull(reader);
    return reader.ReadBool();
  }

  /// <summary>
  /// Rasterize splines into the channel planes. Mutates <paramref name="channels"/>
  /// in place. With a null or empty <paramref name="splineList"/>, this is a
  /// no-op (the typical first-wave path, since <see cref="ReadList"/> only
  /// returns null/throws today).
  /// </summary>
  /// <param name="channels">XYB channel planes, indexed as <c>channels[c][y*width + x]</c>.
  /// Must have exactly 3 entries.</param>
  /// <param name="width">Plane width in pixels.</param>
  /// <param name="height">Plane height in pixels.</param>
  /// <param name="splineList">List of decoded splines, or <c>null</c> for
  /// no-op. An empty list is also a no-op.</param>
  public static void Apply(
    float[][] channels,
    int width,
    int height,
    SplineList splineList
  ) {
    ArgumentNullException.ThrowIfNull(channels);
    if (channels.Length != 3)
      throw new ArgumentException("Expected 3 XYB channel planes.", nameof(channels));
    if (width < 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Width cannot be negative.");
    if (height < 0)
      throw new ArgumentOutOfRangeException(nameof(height), "Height cannot be negative.");

    // Validate plane sizes when not empty. (width=0 or height=0 -> nothing to
    // validate against.)
    if (width > 0 && height > 0) {
      var expected = (long)width * height;
      for (var c = 0; c < 3; c++) {
        ArgumentNullException.ThrowIfNull(channels[c]);
        if (channels[c].LongLength != expected)
          throw new ArgumentException(
            $"Channel {c} has {channels[c].LongLength} pixels but {expected} were expected " +
            $"for a {width}x{height} plane.",
            nameof(channels));
      }
    }

    // No-op cases (the first-wave path, which is also correct for any frame
    // that disables splines).
    if (splineList is null)
      return;
    if (splineList.Splines is null || splineList.Splines.Length == 0)
      return;

    // Full splatting path is not yet implemented. See the implementation
    // notes in `ReadList` for the libjxl pipeline that needs to land here:
    //   * Dequantize each spline's control-point deltas + DCT coefficients
    //     (QuantizedSpline::Dequantize in splines.cc).
    //   * Tessellate via centripetal Catmull-Rom
    //     (DrawCentripetalCatmullRomSpline).
    //   * Walk the tessellation at kDesiredRenderingDistance and call
    //     ContinuousIDCT on the X/Y/B/sigma DCT vectors at each step to
    //     get the per-render-point color and sigma.
    //   * Splat a 2D Gaussian via FastErf (DrawSegment).
    //
    // We could safely no-op here as well (since `ReadList` cannot produce a
    // populated list yet), but a non-null, non-empty SplineList must have
    // been constructed by the caller — flag the gap.
    throw new NotImplementedException(
      "Spline rasterization is not yet implemented. A non-empty SplineList " +
      "was provided but the centripetal Catmull-Rom tessellation, " +
      "ContinuousIDCT for path color/sigma sampling, and 2D Gaussian " +
      "splatting (libjxl Splines::AddTo / DrawSegment in splines.cc) " +
      "are not yet wired into this codec.");
  }
}

/// <summary>
/// A decoded list of splines for a single VarDCT frame. Constructed by
/// <see cref="JxlSplines.ReadList"/> when the frame has splines enabled.
/// </summary>
internal sealed class SplineList {

  /// <summary>The splines in this frame, in bitstream order.</summary>
  public Spline[] Splines { get; init; } = [];
}

/// <summary>
/// A single spline: a parametric curve defined by integer control points
/// and four 32-coefficient DCT vectors that carry the X/Y/B color and
/// Gaussian sigma along the curve's parameter (libjxl
/// <c>struct Spline</c> in splines.h).
/// </summary>
internal sealed class Spline {

  /// <summary>Integer control points in image coordinates. The bitstream
  /// encodes these as a starting position followed by double-delta-coded
  /// offsets; this is the dequantized result.</summary>
  public Point2D[] ControlPoints { get; init; } = [];

  /// <summary>32 DCT coefficients for the X channel along the path
  /// parameter (libjxl <c>color_dct[0]</c>).</summary>
  public float[] Dct32X { get; init; } = [];

  /// <summary>32 DCT coefficients for the Y channel along the path
  /// parameter (libjxl <c>color_dct[1]</c>).</summary>
  public float[] Dct32Y { get; init; } = [];

  /// <summary>32 DCT coefficients for the B channel along the path
  /// parameter (libjxl <c>color_dct[2]</c>).</summary>
  public float[] Dct32B { get; init; } = [];

  /// <summary>32 DCT coefficients for the Gaussian sigma along the path
  /// parameter (libjxl <c>sigma_dct</c>).</summary>
  public float[] Dct32Sigma { get; init; } = [];
}

/// <summary>
/// Integer 2D point used for spline control points (the bitstream rounds
/// to integers; libjxl stores them as <c>float</c> but the dequantization
/// step rounds to <c>int</c> first — see <c>QuantizedSpline::Dequantize</c>
/// in splines.cc).
/// </summary>
internal readonly record struct Point2D(int X, int Y);
