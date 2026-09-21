using System;

namespace FileFormat.Codecs.Indeo.Tests;

/// <summary>
/// The five-three decomposition against the recomposition it claims to invert.
/// </summary>
/// <remarks>
/// A scalable picture only survives the round trip if the encoder's analysis is the exact inverse of
/// the decoder's synthesis. It is not enough for it to be "a" five-three analysis: the recomposition
/// this decoder implements is not the synthesis half of the textbook lifting pair, so a textbook
/// analysis in front of it loses up to 99 of 255 on noise while still looking perfectly plausible on
/// flat and ramp inputs. Everything here drives noise as well as the easy inputs for that reason.
/// <para/>
/// The round trip cannot be asked for more than <see cref="_ALLOWANCE"/>, and that is not a comfort
/// margin. As a map on real numbers the analysis is the exact inverse of the recomposition, but the
/// bitstream carries band samples as integers, and rounding the real coefficients into them moves
/// some reconstructed samples across the boundary of the 64 values the recomposition's final shift
/// collapses onto one byte. One sample value is what that costs, and
/// <see cref="TheAllowanceIsAttainedAndNotSlack"/> pins it down as reached rather than assumed.
/// </remarks>
[TestFixture]
public sealed class IviWaveletTests {

  /// <summary>
  /// The whole error budget of the round trip, in sample values. See the remarks on the fixture for
  /// why it is one and not zero, and why it is one and not more.
  /// </summary>
  private const int _ALLOWANCE = 1;

  private static IviPlane _Plane(short[][] bands, int width, int height) {
    var halfWidth = width >> 1;
    var halfHeight = height >> 1;
    var plane = new IviPlane { Width = width, Height = height, Bands = new IviBand[4] };

    for (var band = 0; band < 4; ++band)
      plane.Bands[band] = new() {
        Width = halfWidth,
        Height = halfHeight,
        AlignedHeight = halfHeight,
        Pitch = halfWidth,
        BufferSize = halfWidth * halfHeight,
        Buffer = bands[band],
      };

    return plane;
  }

  private static byte[] _Recompose(short[][] bands, int width, int height) {
    var pixels = new byte[width * height];
    IviWavelet.Recompose(_Plane(bands, width, height), pixels, width, useHaar: false);
    return pixels;
  }

  private static short[] _Picture(string kind, int width, int height, int seed) {
    var plane = new short[width * height];
    var random = new Random(seed);

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = kind switch {
          "flat" => 128,
          "ramp" => width > 1 ? x * 255 / (width - 1) : 0,
          "smooth" => (int)(128 + 110 * Math.Sin(x * 0.4) * Math.Cos(y * 0.35)),
          "noise" => random.Next(256),
          "checker" => (x + y) % 2 == 0 ? 255 : 0,
          _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        plane[y * width + x] = (short)(Math.Clamp(value, 0, 255) - 128);
      }

    return plane;
  }

  private static int _WorstPixel(short[] source, byte[] reconstructed) {
    var worst = 0;
    for (var i = 0; i < source.Length; ++i)
      worst = Math.Max(worst, Math.Abs(reconstructed[i] - 128 - source[i]));

    return worst;
  }

  /// <summary>
  /// The round trip over pictures this synthesis can actually produce, which reach band values a
  /// photographic plane never does.
  /// </summary>
  [TestCase(8, 8, 1)]
  [TestCase(16, 16, 2)]
  [TestCase(32, 24, 3)]
  [TestCase(2, 2, 4)]
  [TestCase(6, 10, 5)]
  [TestCase(64, 8, 6)]
  [Category("Unit")]
  public void GivenASynthesisedPicture_WhenAnalysedAndRecomposed_ItComesBackWithinTheCoefficientRounding(
    int width,
    int height,
    int seed) {
    var halfWidth = width >> 1;
    var halfHeight = height >> 1;
    var random = new Random(seed);
    var bands = new short[4][];
    for (var band = 0; band < 4; ++band) {
      bands[band] = new short[halfWidth * halfHeight];
      for (var i = 0; i < bands[band].Length; ++i)
        bands[band][i] = (short)random.Next(-400, 401);
    }

    var pixels = _Recompose(bands, width, height);
    var samples = new short[width * height];
    for (var i = 0; i < samples.Length; ++i)
      samples[i] = (short)(pixels[i] - 128);

    var recovered = IviWavelet.Decompose(samples, width, height);
    var again = _Recompose(recovered, width, height);

    Assert.That(
      _WorstPixel(samples, again),
      Is.LessThanOrEqualTo(_ALLOWANCE),
      $"a {width}x{height} picture the synthesis itself produced did not survive the round trip");
  }

  /// <summary>
  /// A picture through the analysis and straight back through the synthesis, with no quantiser
  /// between them. Noise is the case that exposed the transform being the wrong inverse.
  /// </summary>
  [TestCase("flat", 32, 32, 0)]
  [TestCase("ramp", 32, 32, 0)]
  [TestCase("checker", 32, 32, 0)]
  [TestCase("smooth", 32, 32, 1)]
  [TestCase("noise", 32, 32, 1)]
  [TestCase("noise", 16, 16, 1)]
  [TestCase("noise", 64, 48, 1)]
  [TestCase("smooth", 6, 10, 1)]
  [TestCase("noise", 2, 2, 1)]
  [Category("Unit")]
  public void GivenAPicture_WhenAnalysedAndRecomposed_ItComesBackWithinTheCoefficientRounding(
    string kind,
    int width,
    int height,
    int allowed) {
    var source = _Picture(kind, width, height, seed: 7);

    var bands = IviWavelet.Decompose(source, width, height);
    var reconstructed = _Recompose(bands, width, height);

    Assert.That(
      _WorstPixel(source, reconstructed),
      Is.LessThanOrEqualTo(allowed),
      $"a {width}x{height} {kind} picture did not survive the five-three round trip");
  }

  /// <summary>
  /// The allowance is the measured extreme rather than a round number chosen for comfort: flat,
  /// ramp and checkerboard pictures come back with nothing wrong at all, and smooth and noise ones
  /// reach exactly one. Asking for zero everywhere would be a false claim, and asking for more than
  /// one would stop the assertion from noticing a transform that had started to drift.
  /// </summary>
  [Category("Unit")]
  public void TheAllowanceIsAttainedAndNotSlack() {
    var exact = new[] { "flat", "ramp", "checker" };
    var inexact = new[] { "smooth", "noise" };

    foreach (var kind in exact) {
      var source = _Picture(kind, 32, 32, seed: 7);
      Assert.That(
        _WorstPixel(source, _Recompose(IviWavelet.Decompose(source, 32, 32), 32, 32)),
        Is.Zero,
        $"a {kind} picture should need none of the allowance");
    }

    var attained = 0;
    foreach (var kind in inexact) {
      var source = _Picture(kind, 32, 32, seed: 7);
      attained = Math.Max(
        attained,
        _WorstPixel(source, _Recompose(IviWavelet.Decompose(source, 32, 32), 32, 32)));
    }

    Assert.That(
      attained,
      Is.EqualTo(_ALLOWANCE),
      "the allowance is no longer the worst the round trip actually produces, so it has stopped being tight");
  }

  /// <summary>
  /// The allowance above has to bite. A textbook five-three analysis is a plausible thing to put in
  /// front of this recomposition and is what the encoder used to do; it has to fail the same
  /// assertion by a wide margin, or the assertion is measuring nothing.
  /// </summary>
  /// <remarks>
  /// The textbook analysis is reproduced here rather than kept in the encoder so that the comparison
  /// stays available after the encoder stopped using it. It is the lifting form: predict each odd
  /// sample from its two even neighbours, then update the evens from the resulting detail.
  /// </remarks>
  [TestCase("noise", 32, 32)]
  [TestCase("smooth", 32, 32)]
  [Category("Unit")]
  public void GivenTheTextbookAnalysis_TheRoundTripAssertionFailsLoudly(string kind, int width, int height) {
    var source = _Picture(kind, width, height, seed: 7);

    var bands = _TextbookDecompose(source, width, height);
    var reconstructed = _Recompose(bands, width, height);

    Assert.That(
      _WorstPixel(source, reconstructed),
      Is.GreaterThan(8),
      "the textbook analysis reconstructed too well for this test to prove the real assertion bites");
  }

  /// <summary>
  /// Dropping the recomposition's doubled vertical high-pass - that is, assuming it really is the
  /// textbook separable synthesis - has to break the round trip too. This is the single term the
  /// derivation turns on, so it is the one perturbation that has to be shown to matter.
  /// </summary>
  [TestCase("noise", 32, 32)]
  [Category("Unit")]
  public void GivenTheDoubledTermIgnored_TheRoundTripAssertionFailsLoudly(string kind, int width, int height) {
    var source = _Picture(kind, width, height, seed: 7);

    var bands = _SeparableDecompose(source, width, height);
    var reconstructed = _Recompose(bands, width, height);

    Assert.That(
      _WorstPixel(source, reconstructed),
      Is.GreaterThan(8),
      "ignoring the doubled vertical high-pass reconstructed too well for the derivation to be load-bearing");
  }

  private static short[][] _TextbookDecompose(short[] source, int width, int height) {
    var halfWidth = width >> 1;
    var halfHeight = height >> 1;
    var lowRows = new int[halfWidth * height];
    var highRows = new int[halfWidth * height];
    var row = new int[width];
    var low = new int[halfWidth];
    var high = new int[halfWidth];

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x)
        row[x] = source[y * width + x];

      _TextbookAnalyse(row, low, high);
      Array.Copy(low, 0, lowRows, y * halfWidth, halfWidth);
      Array.Copy(high, 0, highRows, y * halfWidth, halfWidth);
    }

    var result = new[] {
      new short[halfWidth * halfHeight],
      new short[halfWidth * halfHeight],
      new short[halfWidth * halfHeight],
      new short[halfWidth * halfHeight],
    };
    var column = new int[height];
    var lowColumn = new int[halfHeight];
    var highColumn = new int[halfHeight];

    for (var x = 0; x < halfWidth; ++x) {
      for (var y = 0; y < height; ++y)
        column[y] = lowRows[y * halfWidth + x];

      _TextbookAnalyse(column, lowColumn, highColumn);
      for (var y = 0; y < halfHeight; ++y) {
        result[0][y * halfWidth + x] = (short)lowColumn[y];
        result[1][y * halfWidth + x] = (short)highColumn[y];
      }

      for (var y = 0; y < height; ++y)
        column[y] = highRows[y * halfWidth + x];

      _TextbookAnalyse(column, lowColumn, highColumn);
      for (var y = 0; y < halfHeight; ++y) {
        result[2][y * halfWidth + x] = (short)lowColumn[y];
        result[3][y * halfWidth + x] = (short)highColumn[y];
      }
    }

    return result;
  }

  private static void _TextbookAnalyse(ReadOnlySpan<int> source, Span<int> low, Span<int> high) {
    var count = source.Length >> 1;

    for (var i = 0; i < count; ++i) {
      var even = source[i << 1];
      var nextEven = source[i + 1 < count ? (i + 1) << 1 : i << 1];
      var odd = source[(i << 1) + 1];
      high[i] = (int)Math.Floor((even + nextEven) * 0.5 + 0.5) - odd;
    }

    for (var i = 0; i < count; ++i) {
      var previousHigh = high[i == 0 ? 0 : i - 1];
      low[i] = 2 * source[i << 1] - (int)Math.Floor((previousHigh + high[i]) * 0.5 + 0.5);
    }
  }

  /// <summary>
  /// The derivation with its one distinguishing term removed: the column inverse of the low-pass and
  /// horizontal-detail pair is run as the plain separable one rather than as the recursion, and the
  /// row correction is dropped with it.
  /// </summary>
  private static short[][] _SeparableDecompose(short[] source, int width, int height) {
    var halfWidth = width >> 1;
    var halfHeight = height >> 1;
    var count = halfWidth * halfHeight;
    var rowEven = new double[halfWidth];
    var rowOdd = new double[halfWidth];
    var rowLow = new double[halfWidth];
    var rowHigh = new double[halfWidth];
    var evenRows = new double[count];
    var oddRows = new double[count];
    var evenDetail = new double[count];
    var oddDetail = new double[count];

    for (var i = 0; i < halfHeight; ++i) {
      var band = i * halfWidth;
      for (var half = 0; half < 2; ++half) {
        var line = ((i << 1) + half) * width;
        for (var j = 0; j < halfWidth; ++j) {
          rowEven[j] = source[line + (j << 1)] * 64.0 + 32.0;
          rowOdd[j] = source[line + (j << 1) + 1] * 64.0 + 32.0;
        }

        _SeparableInvert(rowEven, rowOdd, rowLow, rowHigh, halfWidth);
        Array.Copy(rowLow, 0, half == 0 ? evenRows : oddRows, band, halfWidth);
        Array.Copy(rowHigh, 0, half == 0 ? evenDetail : oddDetail, band, halfWidth);
      }
    }

    var result = new[] { new short[count], new short[count], new short[count], new short[count] };
    var columnEven = new double[halfHeight];
    var columnOdd = new double[halfHeight];
    var columnLow = new double[halfHeight];
    var columnHigh = new double[halfHeight];

    for (var j = 0; j < halfWidth; ++j)
      for (var pair = 0; pair < 2; ++pair) {
        var even = pair == 0 ? evenRows : evenDetail;
        var odd = pair == 0 ? oddRows : oddDetail;
        for (var i = 0; i < halfHeight; ++i) {
          columnEven[i] = even[i * halfWidth + j];
          columnOdd[i] = odd[i * halfWidth + j];
        }

        _SeparableInvert(columnEven, columnOdd, columnLow, columnHigh, halfHeight);
        for (var i = 0; i < halfHeight; ++i) {
          result[pair << 1][i * halfWidth + j] = (short)Math.Round(columnLow[i], MidpointRounding.AwayFromZero);
          result[(pair << 1) + 1][i * halfWidth + j] = (short)Math.Round(columnHigh[i], MidpointRounding.AwayFromZero);
        }
      }

    return result;
  }

  private static void _SeparableInvert(
    ReadOnlySpan<double> even,
    ReadOnlySpan<double> odd,
    Span<double> low,
    Span<double> high,
    int count) {
    for (var i = 0; i < count - 1; ++i)
      high[i] = ((even[i] + even[i + 1]) * 0.5 - odd[i]) * 0.125;

    high[count - 1] = count == 1
      ? (even[0] - odd[0]) * 0.125
      : (even[count - 1] - high[count - 2] - odd[count - 1]) / 7.0;

    for (var i = 0; i < count; ++i)
      low[i] = (even[i] - 2 * high[i] - 2 * high[i > 0 ? i - 1 : 0]) * 0.25;
  }
}
