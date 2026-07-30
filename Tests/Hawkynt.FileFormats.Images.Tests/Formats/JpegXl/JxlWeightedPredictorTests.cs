using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Unit tests for <see cref="JxlWeightedPredictor"/> (ISO/IEC 18181-1 §H.3
/// / §H.4; libjxl <c>weighted::State</c> in
/// <c>lib/jxl/modular/encoding/context_predict.h</c>).
/// </summary>
[TestFixture]
public sealed class JxlWeightedPredictorTests {

  /// <summary>
  /// Construct a 4x4 channel pre-filled with a constant value. Used to exercise
  /// the WP on a perfectly-flat region: all sub-predictors should converge on
  /// the same value, and rounding never changes the answer.
  /// </summary>
  private static JxlChannel _ConstantChannel(int width, int height, int value) {
    var pixels = new int[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = value;
    return new JxlChannel { Width = width, Height = height, Pixels = pixels };
  }

  [Test]
  public void Constructor_RejectsNonPositiveWidth() {
    Assert.Multiple(() => {
      Assert.Throws<ArgumentOutOfRangeException>(() => _ = new JxlWeightedPredictor(0, 1024));
      Assert.Throws<ArgumentOutOfRangeException>(() => _ = new JxlWeightedPredictor(-3, 1024));
    });
  }

  [Test]
  public void Predict_TopLeftPixel_ReturnsZeroOnEmptyContext() {
    // The first pixel has no neighbours: N/W/NE/NW/NN all default to 0.
    // Every sub-predictor returns 0; the weighted average is 0.
    var wp = new JxlWeightedPredictor(width: 4, maxError: 1024);
    var ch = _ConstantChannel(4, 4, value: 0);

    var p = wp.Predict(0, 0, ch);
    Assert.That(p, Is.EqualTo(0));
  }

  [Test]
  public void Predict_FlatRegion_ReturnsNeighbourValue() {
    // After the encoder fills row 0 with `v`, the WP at (1, 1) sees
    // W = N = NE = NW = NN = v. All four sub-predictors yield exactly v
    // in the <<3 domain; the rounded weighted average is v.
    const int v = 10;
    var wp = new JxlWeightedPredictor(width: 4, maxError: 1024);
    var ch = _ConstantChannel(4, 4, value: v);

    // Walk through row 0 to populate the WP's error state along the top.
    for (var x = 0; x < 4; ++x) {
      _ = wp.Predict(x, 0, ch);
      wp.Update(x, 0, v, ch);
    }

    // First pixel of row 1: W is 0 (no W on column 0), but N/NE/NW/NN exist.
    // Walk in raster order to let column 0 establish its error history first.
    _ = wp.Predict(0, 1, ch);
    wp.Update(0, 1, v, ch);

    var p11 = wp.Predict(1, 1, ch);
    Assert.That(p11, Is.EqualTo(v),
      "On a perfectly-flat region the WP must reproduce the neighbour value.");
  }

  [Test]
  public void PredictThenUpdate_ChannelMismatch_Throws() {
    var wp = new JxlWeightedPredictor(width: 4, maxError: 1024);
    var wrongWidth = _ConstantChannel(8, 4, value: 0);
    Assert.Throws<ArgumentException>(() => wp.Predict(0, 0, wrongWidth));
  }

  [Test]
  public void Predict_OutOfRangeCoordinates_Throws() {
    var wp = new JxlWeightedPredictor(width: 4, maxError: 1024);
    var ch = _ConstantChannel(4, 4, value: 0);
    Assert.Multiple(() => {
      Assert.Throws<ArgumentOutOfRangeException>(() => wp.Predict(-1, 0, ch));
      Assert.Throws<ArgumentOutOfRangeException>(() => wp.Predict(4, 0, ch));
      Assert.Throws<ArgumentOutOfRangeException>(() => wp.Predict(0, -1, ch));
      Assert.Throws<ArgumentOutOfRangeException>(() => wp.Predict(0, 4, ch));
    });
  }

  [Test]
  public void Predict_NullChannel_Throws() {
    var wp = new JxlWeightedPredictor(width: 4, maxError: 1024);
    Assert.Throws<ArgumentNullException>(() => wp.Predict(0, 0, null!));
  }

  [Test]
  public void GetProperties_ReturnsSinglePropertyValue() {
    // libjxl exposes exactly one WP-derived property (kWPProp).
    var wp = new JxlWeightedPredictor(width: 4, maxError: 1024);
    var ch = _ConstantChannel(4, 4, value: 0);

    var props = wp.GetProperties(0, 0, ch);
    Assert.Multiple(() => {
      Assert.That(props.Length, Is.EqualTo(1),
        "WP exposes exactly one MA-tree property (libjxl kWPProp).");
      // On the very first pixel the rolling error state is all zero.
      Assert.That(props[0], Is.EqualTo(0));
    });
  }

  [Test]
  public void Predict_HorizontalGradient_ReturnsExtrapolation() {
    // Row 0 = [0, 1, 2, 3]. After populating row 0, the WP at column 0 of
    // row 1 has N=0; further into the row the simple sub-predictor p0 =
    // W + NE - N tracks the linear trend. We verify the prediction falls
    // within the convex hull of the surrounding values, which the libjxl
    // clamp branch guarantees on a sign-aligned error pattern.
    var ch = new JxlChannel {
      Width = 4,
      Height = 2,
      Pixels = new[] {
        0, 1, 2, 3,
        0, 0, 0, 0, // row 1 is the prediction target
      },
    };
    var wp = new JxlWeightedPredictor(width: 4, maxError: 1024);
    for (var x = 0; x < 4; ++x) {
      _ = wp.Predict(x, 0, ch);
      wp.Update(x, 0, ch.Get(x, 0), ch);
    }
    var p0 = wp.Predict(0, 1, ch);
    Assert.That(p0, Is.GreaterThanOrEqualTo(-1).And.LessThanOrEqualTo(2),
      "Prediction at (0,1) lies within the neighbourhood [W=0..NE=1..N=0] window plus rounding slack.");
  }

  [Test]
  public void NumSubPredictors_MatchesLibjxlConstant() {
    Assert.That(JxlWeightedPredictor.NumSubPredictors, Is.EqualTo(4),
      "libjxl weighted::kNumPredictors == 4.");
  }

  [Test]
  public void PredExtraBits_MatchesLibjxlConstant() {
    Assert.That(JxlWeightedPredictor.PredExtraBits, Is.EqualTo(3),
      "libjxl weighted::kPredExtraBits == 3.");
  }
}
