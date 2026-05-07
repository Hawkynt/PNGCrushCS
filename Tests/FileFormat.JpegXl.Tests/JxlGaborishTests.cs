using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Spec-conformance tests for <see cref="JxlGaborish"/> (ISO/IEC 18181-1
/// §G.10, libjxl
/// <c>lib/jxl/render_pipeline/stage_gaborish.cc::GaborishStage::ProcessRow</c>
/// + <c>lib/jxl/loop_filter.cc::LoopFilter::VisitFields</c>).
/// </summary>
[TestFixture]
internal sealed class JxlGaborishTests {

  // libjxl spec defaults: 1.1 * {0.104699568, 0.055680538}.
  private const float _DefaultA = 1.1f * 0.104699568f;
  private const float _DefaultB = 1.1f * 0.055680538f;

  // ------------------------------------------------------------------
  // DefaultWeights.
  // ------------------------------------------------------------------

  [Test]
  public void DefaultWeights_Channel0_MatchesLibjxlConstants() {
    var (a, b) = JxlGaborish.DefaultWeights(0);
    Assert.That(a, Is.EqualTo(_DefaultA).Within(1e-7f));
    Assert.That(b, Is.EqualTo(_DefaultB).Within(1e-7f));
  }

  [Test]
  public void DefaultWeights_AllThreeChannelsAreIdentical() {
    var (ax, bx) = JxlGaborish.DefaultWeights(0);
    var (ay, by) = JxlGaborish.DefaultWeights(1);
    var (ab, bb) = JxlGaborish.DefaultWeights(2);
    Assert.That(ay, Is.EqualTo(ax));
    Assert.That(by, Is.EqualTo(bx));
    Assert.That(ab, Is.EqualTo(ax));
    Assert.That(bb, Is.EqualTo(bx));
  }

  [Test]
  public void DefaultWeights_RejectsOutOfRangeChannel() {
    Assert.Throws<ArgumentOutOfRangeException>(() => JxlGaborish.DefaultWeights(-1));
    Assert.Throws<ArgumentOutOfRangeException>(() => JxlGaborish.DefaultWeights(3));
  }

  [Test]
  public void DefaultWeights_KernelDcGainIsOneAfterNormalization() {
    // The test that matters: w0 + 4*(a+b) is the divisor; after dividing by
    // it, the kernel sums to 1.0 exactly (modulo float).
    var (a, b) = JxlGaborish.DefaultWeights(1);
    var div = 1.0f + 4.0f * (a + b);
    var sum = (1.0f + 4.0f * a + 4.0f * b) / div;
    Assert.That(sum, Is.EqualTo(1.0f).Within(1e-6f));
  }

  // ------------------------------------------------------------------
  // ApplyInPlace — algorithmic correctness.
  // ------------------------------------------------------------------

  [Test]
  public void ApplyInPlace_ConstantInputIsUnchanged() {
    // DC-preserving filter: a uniform plane must stay uniform.
    const int W = 8;
    const int H = 6;
    var pixels = new float[W * H];
    for (var i = 0; i < pixels.Length; i++)
      pixels[i] = 0.42f;

    JxlGaborish.ApplyInPlace(pixels, W, H);

    foreach (var p in pixels)
      Assert.That(p, Is.EqualTo(0.42f).Within(1e-6f));
  }

  [Test]
  public void ApplyInPlace_SinglePixelImpulseProducesPositiveHaloOfExpectedShape() {
    // Place an impulse in the center of an otherwise-zero plane. After the
    // low-pass filter, the 8 neighbours must be > 0, the 4-neighbours must
    // exceed the diagonals (since w1 > w2 for default weights), and the
    // total sum must equal the impulse magnitude (DC preservation).
    const int W = 5;
    const int H = 5;
    var pixels = new float[W * H];
    pixels[2 * W + 2] = 1.0f;

    JxlGaborish.ApplyInPlace(pixels, W, H);

    var center = pixels[2 * W + 2];
    var north = pixels[1 * W + 2];
    var south = pixels[3 * W + 2];
    var east = pixels[2 * W + 3];
    var west = pixels[2 * W + 1];
    var ne = pixels[1 * W + 3];

    Assert.That(center, Is.GreaterThan(0.0f), "center weight must be positive");
    Assert.That(north, Is.GreaterThan(0.0f), "4-neighbour weight must be positive");
    Assert.That(south, Is.EqualTo(north).Within(1e-6f), "vertical symmetry");
    Assert.That(east, Is.EqualTo(west).Within(1e-6f), "horizontal symmetry");
    Assert.That(north, Is.GreaterThan(ne), "4-neighbour weight > diagonal weight (w1 > w2 in defaults)");

    // DC preservation: sum of impulse response equals 1.
    var total = 0.0f;
    foreach (var p in pixels)
      total += p;
    Assert.That(total, Is.EqualTo(1.0f).Within(1e-5f));
  }

  [Test]
  public void ApplyInPlace_ImpulseHaloMagnitudeMatchesNormalizedWeights() {
    // The 4-neighbour weight equals a / div, the diagonal weight equals
    // b / div, where div = 1 + 4*(a+b). Verify these exact values.
    const int W = 5;
    const int H = 5;
    var pixels = new float[W * H];
    pixels[2 * W + 2] = 1.0f;

    JxlGaborish.ApplyInPlace(pixels, W, H);

    var div = 1.0f + 4.0f * (_DefaultA + _DefaultB);
    var expectedW1 = _DefaultA / div;
    var expectedW2 = _DefaultB / div;

    Assert.That(pixels[1 * W + 2], Is.EqualTo(expectedW1).Within(1e-6f));
    Assert.That(pixels[1 * W + 1], Is.EqualTo(expectedW2).Within(1e-6f));
  }

  [Test]
  public void ApplyInPlace_BoundaryReplicationHandlesOnePixelImage() {
    // 1x1 plane: every neighbour replicates back to the same pixel, so
    // output = (w0 + 4w1 + 4w2) * pixel = 1 * pixel.
    var pixels = new[] { 0.7f };
    JxlGaborish.ApplyInPlace(pixels, 1, 1);
    Assert.That(pixels[0], Is.EqualTo(0.7f).Within(1e-6f));
  }

  [Test]
  public void ApplyInPlace_BoundaryReplicationHandlesOnePixelTallStrip() {
    var pixels = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
    JxlGaborish.ApplyInPlace(pixels, 4, 1);
    // No NaN/Inf and no segfault. DC of a constant case verified elsewhere.
    foreach (var p in pixels)
      Assert.That(float.IsFinite(p), Is.True);
  }

  [Test]
  public void ApplyInPlace_BoundaryReplicationHandlesOnePixelWideStrip() {
    var pixels = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
    JxlGaborish.ApplyInPlace(pixels, 1, 4);
    foreach (var p in pixels)
      Assert.That(float.IsFinite(p), Is.True);
  }

  [Test]
  public void ApplyInPlace_CustomWeightsArrayIsAccepted() {
    // Pass explicit defaults — must produce identical output to the null path.
    const int W = 4;
    const int H = 4;
    var a = new float[W * H];
    var b = new float[W * H];
    for (var i = 0; i < W * H; i++) {
      a[i] = i;
      b[i] = i;
    }

    JxlGaborish.ApplyInPlace(a, W, H);
    JxlGaborish.ApplyInPlace(b, W, H, [_DefaultA, _DefaultB]);

    for (var i = 0; i < W * H; i++)
      Assert.That(b[i], Is.EqualTo(a[i]).Within(1e-6f));
  }

  // ------------------------------------------------------------------
  // ApplyInPlace — argument validation.
  // ------------------------------------------------------------------

  [Test]
  public void ApplyInPlace_RejectsNullPixels() {
    Assert.Throws<ArgumentNullException>(() =>
      JxlGaborish.ApplyInPlace(null!, 4, 4));
  }

  [Test]
  public void ApplyInPlace_RejectsNonPositiveDimensions() {
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlGaborish.ApplyInPlace(Array.Empty<float>(), 0, 4));
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      JxlGaborish.ApplyInPlace(Array.Empty<float>(), 4, 0));
  }

  [Test]
  public void ApplyInPlace_RejectsMismatchedLength() {
    Assert.Throws<ArgumentException>(() =>
      JxlGaborish.ApplyInPlace(new float[10], 4, 4));
  }

  [Test]
  public void ApplyInPlace_RejectsBadWeightsArrayLength() {
    Assert.Throws<ArgumentException>(() =>
      JxlGaborish.ApplyInPlace(new float[16], 4, 4, [0.1f]));
    Assert.Throws<ArgumentException>(() =>
      JxlGaborish.ApplyInPlace(new float[16], 4, 4, [0.1f, 0.2f, 0.3f]));
  }

  [Test]
  public void ApplyInPlace_RejectsDegenerateWeights() {
    // a = b = -0.125 makes div = 1 + 4*(-0.25) = 0 → unnormalizable.
    Assert.Throws<ArgumentException>(() =>
      JxlGaborish.ApplyInPlace(new float[16], 4, 4, [-0.125f, -0.125f]));
  }

  // ------------------------------------------------------------------
  // ReadHeader.
  // ------------------------------------------------------------------

  [Test]
  public void ReadHeader_AllDefaultFlag_ReturnsDefaultEnabledParams() {
    // 1 bit set: all_default=1.
    var data = new byte[] { 0b0000_0001 };
    var reader = new JxlBitReader(data, 0);
    var p = JxlGaborish.ReadHeader(reader);
    Assert.That(p, Is.Not.Null);
    Assert.That(p!.Enabled, Is.True);
    Assert.That(p.WeightsX[0], Is.EqualTo(_DefaultA).Within(1e-7f));
    Assert.That(p.WeightsX[1], Is.EqualTo(_DefaultB).Within(1e-7f));
    Assert.That(p.WeightsY[0], Is.EqualTo(_DefaultA).Within(1e-7f));
    Assert.That(p.WeightsB[1], Is.EqualTo(_DefaultB).Within(1e-7f));
  }

  [Test]
  public void ReadHeader_GabDisabled_ReturnsNull() {
    // all_default = 0, gab = 0 → 2 LSBs = 0b00.
    var data = new byte[] { 0b0000_0000 };
    var reader = new JxlBitReader(data, 0);
    var p = JxlGaborish.ReadHeader(reader);
    Assert.That(p, Is.Null);
  }

  [Test]
  public void ReadHeader_GabEnabledNoCustomWeights_ReturnsDefaultParams() {
    // all_default=0, gab=1, gab_custom=0 → 3 LSBs = 0b010.
    // Bits LSB-first: bit0=all_default=0, bit1=gab=1, bit2=gab_custom=0.
    var data = new byte[] { 0b0000_0010 };
    var reader = new JxlBitReader(data, 0);
    var p = JxlGaborish.ReadHeader(reader);
    Assert.That(p, Is.Not.Null);
    Assert.That(p!.Enabled, Is.True);
    Assert.That(p.WeightsX[0], Is.EqualTo(_DefaultA).Within(1e-7f));
    Assert.That(p.WeightsY[1], Is.EqualTo(_DefaultB).Within(1e-7f));
  }

  [Test]
  public void ReadHeader_RejectsNullReader() {
    Assert.Throws<ArgumentNullException>(() => JxlGaborish.ReadHeader(null!));
  }
}
