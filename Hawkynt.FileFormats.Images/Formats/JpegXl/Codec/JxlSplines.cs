using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// The splines layer (ISO/IEC 18181-1 §G.11; libjxl <c>lib/jxl/splines.cc</c>).
/// </summary>
/// <remarks>
/// A spline is a thin line-like feature the encoder decided was cheaper to draw
/// than to code as blocks: a run of control points, plus four 32-coefficient
/// curves giving the colour and the Gaussian width along the path. The decoder
/// tessellates the path, walks it at one pixel a step, and splats a normalised
/// Gaussian at each step. A frame states that it has them in its header flags —
/// there is no separate flag in this section, which is what the earlier stub
/// here assumed.
///
/// <para>The two approximations libjxl uses are ported rather than replaced by
/// the exact functions. Where a spline is most of the picture — and a 2,048
/// square file of 81 bytes is nothing but splines — the difference between
/// <c>FastErff</c> and a real error function is the difference between agreeing
/// with libjxl and not.</para>
/// </remarks>
internal static class JxlSplines {

  private const int _QuantizationAdjustmentContext = 0;
  private const int _StartingPositionContext = 1;
  private const int _NumSplinesContext = 2;
  private const int _NumControlPointsContext = 3;
  private const int _ControlPointsContext = 4;
  private const int _DctContext = 5;

  /// <summary>Number of entropy contexts (libjxl <c>kNumSplineContexts</c>).</summary>
  internal const int NumSplineContexts = 6;

  /// <summary>libjxl <c>kDesiredRenderingDistance</c>: render points along the
  /// tessellated curve are this many pixels apart.</summary>
  internal const float DesiredRenderingDistance = 1.0f;

  /// <summary>X, Y, B, sigma (libjxl <c>kChannelWeight</c>).</summary>
  internal static readonly float[] ChannelWeights = [0.0042f, 0.075f, 0.07f, 0.3333f];

  private const int _MaxNumControlPoints = 1 << 20;
  private const int _MaxNumControlPointsPerPixelRatio = 2;
  private const long _SplinePosLimit = 1L << 23;
  private const long _DeltaLimit = 1L << 30;

  private const float _Sqrt2 = 1.41421356237309504880168872420969808f;
  private const float _SqrtHalf = 0.707106781186547524400844362104849039f;
  private const float _Pi = 3.14159265358979323846264338327950288f;

  /// <summary>
  /// Read the spline list. The caller has already established from the frame
  /// header that the frame has one; this section carries no flag of its own.
  /// </summary>
  /// <param name="reader">Positioned at the start of the spline section.</param>
  /// <param name="numPixels">Width times height, which bounds how many control
  /// points the frame may state.</param>
  public static SplineList Decode(JxlBitReader reader, long numPixels) {
    ArgumentNullException.ThrowIfNull(reader);

    var entropy = JxlEntropyDecoder.Read(reader, NumSplineContexts);

    var maxControlPoints = (int)Math.Min(_MaxNumControlPoints, numPixels / _MaxNumControlPointsPerPixelRatio);
    var stated = entropy.ReadInt(_NumSplinesContext);
    if (stated < 0 || stated > maxControlPoints || stated + 1 > maxControlPoints)
      throw new InvalidDataException($"A frame states {stated} splines, which is more than it has room for.");
    var numSplines = stated + 1;

    var startingPoints = _DecodeStartingPoints(entropy, numSplines);
    var quantizationAdjustment = (int)_UnpackSigned(entropy.ReadInt(_QuantizationAdjustmentContext));

    var quantized = new QuantizedSpline[numSplines];
    // libjxl seeds the running total with the spline count, so the starting
    // points count against the same budget as the control points.
    var totalControlPoints = numSplines;
    for (var i = 0; i < numSplines; ++i)
      quantized[i] = _DecodeSpline(entropy, maxControlPoints, ref totalControlPoints);

    if (!entropy.CheckFinalState())
      throw new InvalidDataException("The spline section did not end where its entropy coder says it should.");

    return new SplineList {
      StartingPoints = startingPoints,
      Quantized = quantized,
      QuantizationAdjustment = quantizationAdjustment,
    };
  }

  private static Point2D[] _DecodeStartingPoints(JxlEntropyDecoder entropy, int numSplines) {
    var points = new Point2D[numSplines];
    long lastX = 0;
    long lastY = 0;
    for (var i = 0; i < numSplines; ++i) {
      var dx = entropy.ReadInt(_StartingPositionContext);
      var dy = entropy.ReadInt(_StartingPositionContext);
      // The first point is stated outright; the rest are signed deltas from it.
      var x = i == 0 ? dx : _UnpackSigned(dx) + lastX;
      var y = i == 0 ? dy : _UnpackSigned(dy) + lastY;
      _CheckPosition(x, y);
      points[i] = new Point2D((int)x, (int)y);
      lastX = x;
      lastY = y;
    }

    return points;
  }

  private static QuantizedSpline _DecodeSpline(JxlEntropyDecoder entropy, int maxControlPoints, ref int total) {
    var count = entropy.ReadInt(_NumControlPointsContext);
    if (count < 0 || count > maxControlPoints)
      throw new InvalidDataException($"A spline states {count} control points, which is more than the frame allows.");
    total += count;
    if (total > maxControlPoints)
      throw new InvalidDataException($"The splines state {total} control points between them, which is more than the frame allows.");

    var deltas = new (long X, long Y)[count];
    for (var i = 0; i < count; ++i) {
      var x = _UnpackSigned(entropy.ReadInt(_ControlPointsContext));
      var y = _UnpackSigned(entropy.ReadInt(_ControlPointsContext));
      if (x >= _DeltaLimit || x <= -_DeltaLimit || y >= _DeltaLimit || y <= -_DeltaLimit)
        throw new InvalidDataException("A spline's control point step is out of bounds.");
      deltas[i] = (x, y);
    }

    var color = new int[3][];
    for (var c = 0; c < 3; ++c)
      color[c] = _DecodeDct(entropy);

    return new QuantizedSpline {
      ControlPointDeltas = deltas,
      ColorDct = color,
      SigmaDct = _DecodeDct(entropy),
    };
  }

  private static int[] _DecodeDct(JxlEntropyDecoder entropy) {
    var dct = new int[32];
    for (var i = 0; i < 32; ++i) {
      var value = _UnpackSigned(entropy.ReadInt(_DctContext));
      if (value == int.MinValue)
        throw new InvalidDataException("A spline coefficient is the one value that cannot be negated.");
      dct[i] = (int)value;
    }

    return dct;
  }

  /// <summary>
  /// Turn the decoded list into the segments that get drawn, which is where the
  /// colour correlation enters: a spline states its colour relative to the
  /// luma channel exactly as a block does.
  /// </summary>
  public static IReadOnlyList<SplineSegment> BuildSegments(SplineList list, int width, int height, float yToX, float yToB) {
    ArgumentNullException.ThrowIfNull(list);

    var segments = new List<SplineSegment>();
    long areaReached = 0;
    var imageSize = (long)width * height;

    for (var i = 0; i < list.Quantized.Length; ++i) {
      var spline = _Dequantize(list.Quantized[i], list.StartingPoints[i], list.QuantizationAdjustment,
        yToX, yToB, imageSize, ref areaReached);

      // Two control points in the same place leave the curve's direction
      // undefined, and the tessellation divides by the gap between them.
      for (var k = 1; k < spline.ControlPoints.Length; ++k)
        if (spline.ControlPoints[k] == spline.ControlPoints[k - 1])
          throw new InvalidDataException($"Spline {i} states the same control point twice in a row.");

      var tessellated = _CatmullRom(spline.ControlPoints);
      var drawPoints = _EquallySpaced(tessellated);
      if (drawPoints.Count == 0)
        continue;

      var arcLength = (drawPoints.Count - 2) * DesiredRenderingDistance + drawPoints[^1].Multiplier;
      if (arcLength <= 0.0f)
        continue;

      _SegmentsFromPoints(height, spline, drawPoints, arcLength, segments);
    }

    return segments;
  }

  /// <summary>Draw the segments onto three planes.</summary>
  public static void AddTo(IReadOnlyList<SplineSegment> segments, float[][] planes, int width, int height) {
    ArgumentNullException.ThrowIfNull(segments);
    ArgumentNullException.ThrowIfNull(planes);
    if (planes.Length < 3)
      throw new ArgumentException("Splines are drawn onto three planes.", nameof(planes));

    foreach (var segment in segments) {
      var start = (long)MathF.Round(segment.CenterX - segment.MaximumDistance);
      var end = (long)MathF.Round(segment.CenterX + segment.MaximumDistance);
      if (end < 0 || start >= width)
        continue;

      var y0 = (long)MathF.Round(segment.CenterY - segment.MaximumDistance);
      var y1 = (long)MathF.Round(segment.CenterY + segment.MaximumDistance) + 1;
      y0 = Math.Max(y0, 0);
      y1 = Math.Min(y1, height);

      var x0 = (int)Math.Max(start, 0);
      var x1 = (int)Math.Min(end + 1, width);

      for (var y = y0; y < y1; ++y)
      for (var x = x0; x < x1; ++x) {
        var dx = x - segment.CenterX;
        var dy = y - segment.CenterY;
        var distance = MathF.Sqrt(MathF.FusedMultiplyAdd(dx, dx, dy * dy));
        // The Gaussian is integrated over the pixel rather than sampled at its
        // centre, which is what the two error functions are doing.
        var factor =
          _FastErf(MathF.FusedMultiplyAdd(distance, 0.5f, 0.353553391f) * segment.InvSigma)
          - _FastErf(MathF.FusedMultiplyAdd(distance, 0.5f, -0.353553391f) * segment.InvSigma);
        var intensity = segment.SigmaOverFourTimesIntensity * factor * factor;

        var at = (int)(y * width + x);
        for (var c = 0; c < 3; ++c)
          planes[c][at] += segment.Color[c] * intensity;
      }
    }
  }

  private static _Spline _Dequantize(
    QuantizedSpline quantized, Point2D startingPoint, int quantizationAdjustment,
    float yToX, float yToB, long imageSize, ref long areaReached
  ) {
    var areaLimit = Math.Min(1024L * imageSize + (1L << 32), 1L << 42);

    var points = new List<Point2D>(quantized.ControlPointDeltas.Length + 1);
    _CheckPosition(startingPoint.X, startingPoint.Y);
    var currentX = startingPoint.X;
    var currentY = startingPoint.Y;
    points.Add(new Point2D(currentX, currentY));

    // The steps are stated as changes to the step, so each one is accumulated
    // twice: once into the step and once into the position.
    var deltaX = 0L;
    var deltaY = 0L;
    var manhattan = 0L;
    foreach (var (dx, dy) in quantized.ControlPointDeltas) {
      deltaX += dx;
      deltaY += dy;
      manhattan += Math.Abs(deltaX) + Math.Abs(deltaY);
      if (manhattan > areaLimit)
        throw new InvalidDataException($"A spline walks {manhattan} pixels, which is further than the frame allows.");
      _CheckPosition(deltaX, deltaY);
      currentX = (int)(currentX + deltaX);
      currentY = (int)(currentY + deltaY);
      _CheckPosition(currentX, currentY);
      points.Add(new Point2D(currentX, currentY));
    }

    var invQuant = _InverseAdjustedQuant(quantizationAdjustment);
    var color = new float[3][];
    for (var c = 0; c < 3; ++c) {
      color[c] = new float[32];
      for (var i = 0; i < 32; ++i) {
        // The constant coefficient carries a factor of root two so that the
        // interpolation below can treat all 32 alike.
        var inverseDctFactor = i == 0 ? _SqrtHalf : 1.0f;
        color[c][i] = quantized.ColorDct[c][i] * inverseDctFactor * ChannelWeights[c] * invQuant;
      }
    }

    for (var i = 0; i < 32; ++i) {
      color[0][i] += yToX * color[1][i];
      color[2][i] += yToB * color[1][i];
    }

    // The area estimate below is libjxl's guard against a file that states a
    // few bytes of spline and asks for years of drawing.
    var colorTotals = new long[3];
    for (var c = 0; c < 3; ++c)
      for (var i = 0; i < 32; ++i)
        colorTotals[c] += (long)MathF.Ceiling(invQuant * Math.Abs(quantized.ColorDct[c][i]));
    colorTotals[0] += (long)MathF.Ceiling(Math.Abs(yToX)) * colorTotals[1];
    colorTotals[2] += (long)MathF.Ceiling(Math.Abs(yToB)) * colorTotals[1];
    var maxColor = Math.Max(colorTotals[1], Math.Max(colorTotals[0], colorTotals[2]));
    var logColor = Math.Max(1L, _CeilLog2NonZero(1L + maxColor));

    var weightLimit = MathF.Ceiling(MathF.Sqrt((float)areaLimit / logColor / Math.Max(1L, manhattan)));

    var sigma = new float[32];
    long widthEstimate = 0;
    for (var i = 0; i < 32; ++i) {
      var inverseDctFactor = i == 0 ? _SqrtHalf : 1.0f;
      sigma[i] = quantized.SigmaDct[i] * inverseDctFactor * ChannelWeights[3] * invQuant;
      var weightF = MathF.Ceiling(invQuant * Math.Abs(quantized.SigmaDct[i]));
      var weight = (long)Math.Min(weightLimit, Math.Max(1.0f, weightF));
      widthEstimate += weight * weight * logColor;
    }

    areaReached += widthEstimate * manhattan;
    if (areaReached > areaLimit)
      throw new InvalidDataException($"The splines cover {areaReached}, which is more than the frame allows.");

    return new _Spline {
      ControlPoints = points.ToArray(),
      ColorDct = color,
      SigmaDct = sigma,
    };
  }

  /// <summary>
  /// Centripetal Catmull-Rom through the control points, sixteen steps a span
  /// (libjxl <c>DrawCentripetalCatmullRomSpline</c>).
  /// </summary>
  private static List<(float X, float Y)> _CatmullRom(Point2D[] controlPoints) {
    var result = new List<(float X, float Y)>();
    if (controlPoints.Length == 0)
      return result;
    if (controlPoints.Length == 1) {
      result.Add((controlPoints[0].X, controlPoints[0].Y));
      return result;
    }

    const int steps = 16;
    // The ends are extended by reflecting the first and last step, so that
    // every span has two neighbours to be shaped by.
    var points = new List<(float X, float Y)>(controlPoints.Length + 2);
    points.Add((
      controlPoints[0].X + (controlPoints[0].X - controlPoints[1].X),
      controlPoints[0].Y + (controlPoints[0].Y - controlPoints[1].Y)));
    foreach (var point in controlPoints)
      points.Add((point.X, point.Y));
    var last = controlPoints[^1];
    var beforeLast = controlPoints[^2];
    points.Add((last.X + (last.X - beforeLast.X), last.Y + (last.Y - beforeLast.Y)));

    for (var start = 0; start + 3 < points.Count; ++start) {
      var p0 = points[start];
      var p1 = points[start + 1];
      var p2 = points[start + 2];
      var p3 = points[start + 3];
      result.Add(p1);

      var d = new float[3];
      var t = new float[4];
      var quad = new[] { p0, p1, p2, p3 };
      t[0] = 0.0f;
      for (var k = 0; k < 3; ++k) {
        // The centripetal parameterisation: the root of the distance, which is
        // what keeps the curve from looping on a sharp corner.
        d[k] = MathF.Sqrt(_Hypot(quad[k + 1].X - quad[k].X, quad[k + 1].Y - quad[k].Y));
        t[k + 1] = t[k] + d[k];
      }

      for (var i = 1; i < steps; ++i) {
        var tt = d[0] + (float)i / steps * d[1];
        var a = new (float X, float Y)[3];
        for (var k = 0; k < 3; ++k) {
          var f = (tt - t[k]) / d[k];
          a[k] = (quad[k].X + f * (quad[k + 1].X - quad[k].X), quad[k].Y + f * (quad[k + 1].Y - quad[k].Y));
        }

        var b = new (float X, float Y)[2];
        for (var k = 0; k < 2; ++k) {
          var f = (tt - t[k]) / (d[k] + d[k + 1]);
          b[k] = (a[k].X + f * (a[k + 1].X - a[k].X), a[k].Y + f * (a[k + 1].Y - a[k].Y));
        }

        var g = (tt - t[1]) / d[1];
        result.Add((b[0].X + g * (b[1].X - b[0].X), b[0].Y + g * (b[1].Y - b[0].Y)));
      }
    }

    result.Add(points[^2]);
    return result;
  }

  /// <summary>
  /// Walk the tessellated path a pixel at a time (libjxl
  /// <c>ForEachEquallySpacedPoint</c>).
  /// </summary>
  private static List<(float X, float Y, float Multiplier)> _EquallySpaced(List<(float X, float Y)> points) {
    var result = new List<(float X, float Y, float Multiplier)>();
    if (points.Count == 0)
      return result;

    var current = points[0];
    result.Add((current.X, current.Y, DesiredRenderingDistance));

    var next = 0;
    while (next < points.Count) {
      var previous = current;
      var fromPrevious = 0.0f;
      for (;;) {
        if (next == points.Count) {
          // The last point keeps whatever length is left over, which is what
          // the arc length is measured from.
          result.Add((previous.X, previous.Y, fromPrevious));
          return result;
        }

        var target = points[next];
        var toNext = MathF.Sqrt(
          (target.X - previous.X) * (target.X - previous.X) + (target.Y - previous.Y) * (target.Y - previous.Y));
        if (fromPrevious + toNext >= DesiredRenderingDistance) {
          var f = (DesiredRenderingDistance - fromPrevious) / toNext;
          current = (previous.X + f * (target.X - previous.X), previous.Y + f * (target.Y - previous.Y));
          result.Add((current.X, current.Y, DesiredRenderingDistance));
          break;
        }

        fromPrevious += toNext;
        previous = target;
        ++next;
      }
    }

    return result;
  }

  private static void _SegmentsFromPoints(
    int height, _Spline spline, List<(float X, float Y, float Multiplier)> points,
    float arcLength, List<SplineSegment> segments
  ) {
    var inverseArcLength = 1.0f / arcLength;
    for (var k = 0; k < points.Count; ++k) {
      var (x, y, multiplier) = points[k];
      var along = Math.Min(1.0f, k * DesiredRenderingDistance * inverseArcLength);

      var color = new float[3];
      for (var c = 0; c < 3; ++c)
        color[c] = _ContinuousIdct(spline.ColorDct[c], 31 * along);
      var sigma = _ContinuousIdct(spline.SigmaDct, 31 * along);

      _ComputeSegment(height, x, y, multiplier, color, sigma, segments);
    }
  }

  private static void _ComputeSegment(
    int height, float centerX, float centerY, float intensity, float[] color, float sigma, List<SplineSegment> segments
  ) {
    if (!float.IsFinite(sigma) || sigma == 0.0f || !float.IsFinite(1.0f / sigma) || !float.IsFinite(intensity))
      return;

    // How far out the Gaussian is still drawn: to where it falls below ten to
    // the minus this. libjxl has a faster setting of 3 behind a build flag, but
    // the flag defaults to the precise one and a stock djxl uses 5. Choosing 3
    // costs only the faint outer edge of each splat, which is why it shows as a
    // few hundredths of a level on a picture made entirely of splines rather
    // than as anything obvious.
    const float distanceExponent = 5.0f;
    var maxColor = 0.01f;
    for (var c = 0; c < 3; ++c)
      maxColor = Math.Max(maxColor, Math.Abs(color[c] * intensity));

    var maximumDistance = MathF.Sqrt(
      -2.0f * sigma * sigma * (MathF.Log(0.1f) * distanceExponent - MathF.Log(maxColor)));

    var y0 = Math.Max((long)MathF.Round(centerY - maximumDistance), 0);
    var y1 = Math.Min((long)MathF.Round(centerY + maximumDistance) + 1, height);
    if (y1 <= y0)
      return;

    segments.Add(new SplineSegment {
      CenterX = centerX,
      CenterY = centerY,
      Color = color,
      InvSigma = 1.0f / sigma,
      SigmaOverFourTimesIntensity = 0.25f * sigma * intensity,
      MaximumDistance = maximumDistance,
    });
  }

  /// <summary>
  /// Sample a 32-coefficient curve at a point between its samples (libjxl
  /// <c>ContinuousIDCT</c>): a DCT-3 scaled so a lone first coefficient gives a
  /// constant.
  /// </summary>
  private static float _ContinuousIdct(float[] dct, float t) {
    var result = 0.0f;
    var tAndHalf = t + 0.5f;
    for (var i = 0; i < 32; ++i) {
      var cos = _FastCos(_Pi / 32.0f * i * tAndHalf);
      result = MathF.FusedMultiplyAdd(_Sqrt2, dct[i] * cos, result);
    }

    return result;
  }

  /// <summary>libjxl <c>FastCosf</c>, L1 error 7e-5.</summary>
  private static float _FastCos(float x) {
    // Down to [0, 2pi), then to [0, pi], then to [0, pi/2].
    var reduced = x - MathF.Floor(x * (0.5f / _Pi)) * (_Pi * 2.0f);
    var toPi = Math.Min(reduced, _Pi * 2.0f - reduced);
    var abovePiHalf = toPi >= _Pi / 2.0f;
    var toPiHalf = abovePiHalf ? _Pi - toPi : toPi;

    // A Taylor-like fit on a quarter of the angle, scaled so the two doubling
    // steps that follow are cheap.
    var xs = toPiHalf * 0.25f;
    var x2 = xs * xs;
    var x4 = x2 * x2;
    var prescaled = MathF.FusedMultiplyAdd(x4, 0.06960438f,
      MathF.FusedMultiplyAdd(x2, -0.84087373f, 1.68179268f));
    var scale1 = MathF.FusedMultiplyAdd(prescaled, prescaled, -1.414213562f);
    var scale2 = MathF.FusedMultiplyAdd(scale1, scale1, -1.0f);
    return abovePiHalf ? -scale2 : scale2;
  }

  /// <summary>libjxl <c>FastErff</c>, L1 error 7e-4.</summary>
  private static float _FastErf(float x) {
    var negative = x <= 0.0f;
    var absx = Math.Abs(x);
    var denom = MathF.FusedMultiplyAdd(absx, 7.77394369e-02f, 2.05260015e-04f);
    denom = MathF.FusedMultiplyAdd(denom, absx, 2.32120216e-01f);
    denom = MathF.FusedMultiplyAdd(denom, absx, 2.77820801e-01f);
    denom = MathF.FusedMultiplyAdd(denom, absx, 1.0f);
    denom *= denom;
    var inverse = 1.0f / denom;
    var result = -MathF.FusedMultiplyAdd(inverse, inverse, -1.0f);
    return negative ? -result : result;
  }

  private static float _Hypot(float x, float y) => MathF.Sqrt(x * x + y * y);

  private static float _InverseAdjustedQuant(int adjustment)
    => adjustment >= 0 ? 1.0f / (1.0f + 0.125f * adjustment) : 1.0f - 0.125f * adjustment;

  private static long _CeilLog2NonZero(long value) {
    var bits = 0;
    while (value > 1) {
      value >>= 1;
      ++bits;
    }

    return bits;
  }

  private static void _CheckPosition(long x, long y) {
    if (x >= _SplinePosLimit || x <= -_SplinePosLimit || y >= _SplinePosLimit || y <= -_SplinePosLimit)
      throw new InvalidDataException($"A spline point at {x},{y} is outside anything a picture could hold.");
  }

  /// <summary>
  /// The zig-zag the format packs signed values in: even is positive, odd is
  /// negative.
  /// </summary>
  /// <remarks>
  /// The cast back to a signed 32-bit value is the whole point. The exclusive
  /// or produces all-ones for a negative, which is that value's two's
  /// complement only while it is read as signed and the same width — widen it
  /// unsigned first and every negative becomes four billion instead.
  /// </remarks>
  private static long _UnpackSigned(int packed) {
    var u = (uint)packed;
    return (int)((u >> 1) ^ (~(u & 1) + 1));
  }

  private sealed class _Spline {
    public Point2D[] ControlPoints { get; init; } = [];
    public float[][] ColorDct { get; init; } = [];
    public float[] SigmaDct { get; init; } = [];
  }
}

/// <summary>
/// One spline as the bitstream states it: control points as changes to the
/// step, and four curves still in quantised units.
/// </summary>
internal sealed class QuantizedSpline {
  public (long X, long Y)[] ControlPointDeltas { get; init; } = [];
  public int[][] ColorDct { get; init; } = [];
  public int[] SigmaDct { get; init; } = [];
}

/// <summary>A decoded spline list, before the colour correlation is known.</summary>
internal sealed class SplineList {
  public Point2D[] StartingPoints { get; init; } = [];
  public QuantizedSpline[] Quantized { get; init; } = [];
  public int QuantizationAdjustment { get; init; }
}

/// <summary>
/// One Gaussian to splat: where it goes, how wide it is, and what colour.
/// </summary>
internal sealed class SplineSegment {
  public float CenterX { get; init; }
  public float CenterY { get; init; }
  public float MaximumDistance { get; init; }
  public float InvSigma { get; init; }
  public float SigmaOverFourTimesIntensity { get; init; }
  public float[] Color { get; init; } = [];
}

/// <summary>Integer control point, as the bitstream states it.</summary>
internal readonly record struct Point2D(int X, int Y);
