using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Tests for <see cref="JxlEpf"/> — the Edge-Preserving Filter loop filter for
/// JPEG XL VarDCT (ISO/IEC 18181-1 §G.10).
///
/// <para>libjxl reference: <c>lib/jxl/epf.cc</c>, <c>lib/jxl/loop_filter.cc</c>
/// (field defaults), <c>lib/jxl/epf.h</c> (kInvSigmaNum constant).</para>
/// </summary>
[TestFixture]
internal sealed class JxlEpfTests {

  // -----------------------------------------------------------------------
  // ReadHeader
  // -----------------------------------------------------------------------

  /// <summary>epf_iters = 0 (2-bit field) signals EPF disabled — header reader
  /// returns null and consumes only the 2 bits. Mirrors libjxl
  /// LoopFilter::VisitFields short-circuit.</summary>
  [Test]
  public void ReadHeader_EpfItersZero_ReturnsNull() {
    // 2 bits set to 0, with one trailing byte to keep the reader happy.
    var data = new byte[] { 0b00000000, 0x00 };
    var reader = new JxlBitReader(data, 0);

    var result = JxlEpf.ReadHeader(reader);

    Assert.That(result, Is.Null);
  }

  /// <summary>epf_iters = 1 with no custom sharpness/weight/sigma flags
  /// (i.e. three Bool false bits after the 2-bit iters field) returns a
  /// populated EpfParams with the libjxl defaults.</summary>
  [Test]
  public void ReadHeader_EpfItersOne_AllDefaults_ReturnsDefaultParams() {
    // Bits LSB-first: iters(2)=01, sharp_custom(1)=0, weight_custom(1)=0,
    // sigma_custom(1)=0 → 5 bits of "1, 0, 0, 0, 0" → 0b00001 = 0x01.
    var data = new byte[] { 0b00000001, 0x00 };
    var reader = new JxlBitReader(data, 0);

    var result = JxlEpf.ReadHeader(reader);

    Assert.That(result, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(result!.Iters, Is.EqualTo(1));
      Assert.That(result.Sharpness.Length, Is.EqualTo(8));
      Assert.That(result.Sharpness[0], Is.EqualTo(0.0f).Within(1e-6f));
      Assert.That(result.Sharpness[7], Is.EqualTo(1.0f).Within(1e-6f));
      Assert.That(result.SigmaMul, Is.EqualTo(0.46f).Within(1e-6f));
      Assert.That(result.Pass0SigmaCircle, Is.EqualTo(0.9f).Within(1e-6f));
      Assert.That(result.Pass2SigmaCircle, Is.EqualTo(6.5f).Within(1e-6f));
      Assert.That(result.Pass1SigmaScale, Is.EqualTo(0.45f).Within(1e-6f));
      Assert.That(result.Pass2SigmaScale, Is.EqualTo(0.60f).Within(1e-6f));
    });
  }

  /// <summary>iters in {2, 3} parses successfully (still defaults) — these are
  /// valid header values even though Apply rejects them.</summary>
  [TestCase((byte)0b00000010, 2)] // iters=2, all-default flags
  [TestCase((byte)0b00000011, 3)] // iters=3, all-default flags
  public void ReadHeader_EpfItersTwoOrThree_AllDefaults_ParsesIters(byte first, int expected) {
    var data = new byte[] { first, 0x00 };
    var reader = new JxlBitReader(data, 0);

    var result = JxlEpf.ReadHeader(reader);

    Assert.That(result, Is.Not.Null);
    Assert.That(result!.Iters, Is.EqualTo(expected));
  }

  [Test]
  public void ReadHeader_NullReader_Throws()
    => Assert.Throws<ArgumentNullException>(() => JxlEpf.ReadHeader(null!));

  // -----------------------------------------------------------------------
  // Apply — disabled / no-op cases
  // -----------------------------------------------------------------------

  /// <summary>Apply with Iters=0 is a no-op — input pixels unchanged.</summary>
  [Test]
  public void Apply_ItersZero_IsNoOp() {
    const int w = 16, h = 16;
    var x = _MakeRamp(w * h, 0.0f);
    var y = _MakeRamp(w * h, 100.0f);
    var b = _MakeRamp(w * h, 200.0f);
    var snapshotX = (float[])x.Clone();
    var snapshotY = (float[])y.Clone();
    var snapshotB = (float[])b.Clone();
    var sigma = new float[(w / 8) * (h / 8)]; // all zeros
    var p = new EpfParams { Iters = 0, Sharpness = new float[8] };

    JxlEpf.Apply(new[] { x, y, b }, w, h, sigma, w / 8, h / 8, p);

    Assert.That(x, Is.EqualTo(snapshotX));
    Assert.That(y, Is.EqualTo(snapshotY));
    Assert.That(b, Is.EqualTo(snapshotB));
  }

  /// <summary>Apply with Iters=1 and all-zero sigma blocks must not modify
  /// the image (every block is "skip" per the sigma &lt;= 0 sentinel).</summary>
  [Test]
  public void Apply_AllZeroSigma_IsNoOp() {
    const int w = 16, h = 16;
    var x = _MakeRamp(w * h, 0.0f);
    var y = _MakeRamp(w * h, 50.0f);
    var b = _MakeRamp(w * h, 100.0f);
    var snapshotX = (float[])x.Clone();
    var snapshotY = (float[])y.Clone();
    var snapshotB = (float[])b.Clone();
    var sigma = new float[(w / 8) * (h / 8)]; // all 0
    var p = new EpfParams { Iters = 1, Sharpness = new float[8] };

    JxlEpf.Apply(new[] { x, y, b }, w, h, sigma, w / 8, h / 8, p);

    Assert.Multiple(() => {
      for (var i = 0; i < w * h; i++) {
        Assert.That(x[i], Is.EqualTo(snapshotX[i]).Within(1e-6f));
        Assert.That(y[i], Is.EqualTo(snapshotY[i]).Within(1e-6f));
        Assert.That(b[i], Is.EqualTo(snapshotB[i]).Within(1e-6f));
      }
    });
  }

  // -----------------------------------------------------------------------
  // Apply — actual filtering (Iters=1 with non-zero sigma)
  // -----------------------------------------------------------------------

  /// <summary>Non-zero sigma + a sharp single-pixel impulse: the filter pulls
  /// the impulse pixel toward its neighbours' average. Centre value must
  /// strictly decrease (in magnitude toward neighbour mean = 0).</summary>
  [Test]
  public void Apply_NonZeroSigma_ImpulseSmoothsToward_NeighbourAverage() {
    const int w = 16, h = 16;
    var x = new float[w * h];
    var y = new float[w * h];
    var b = new float[w * h];

    // Single small impulse at (8, 8) on the X channel. (Sigma is stored as
    // 1/raw_sigma in libjxl, so the Gaussian's argument is diff * sigma; small
    // sigma + small impulse → noticeable but not annihilating attenuation.)
    var centre = 8 * w + 8;
    x[centre] = 1.0f;

    // Capture the original centre value for comparison.
    var originalCentre = x[centre];

    // sigma_inv = 0.3 → moderate smoothing for impulse magnitude 1.
    var sigma = new float[(w / 8) * (h / 8)];
    for (var i = 0; i < sigma.Length; i++) sigma[i] = 0.3f;
    var p = new EpfParams { Iters = 1, Sharpness = new float[8] };

    JxlEpf.Apply(new[] { x, y, b }, w, h, sigma, w / 8, h / 8, p);

    // Centre must move toward the neighbour average (which is 0).
    Assert.That(x[centre], Is.LessThan(originalCentre), "Impulse should be attenuated.");
    Assert.That(x[centre], Is.GreaterThan(0.0f), "Impulse should not be fully erased in one tap.");
  }

  /// <summary>Constant-valued image: filter must preserve the constant
  /// regardless of sigma (a flat patch has zero L1 differences and unit
  /// weights everywhere).</summary>
  [Test]
  public void Apply_NonZeroSigma_ConstantImage_Unchanged() {
    const int w = 16, h = 16;
    var x = new float[w * h];
    var y = new float[w * h];
    var b = new float[w * h];
    for (var i = 0; i < w * h; i++) {
      x[i] = 7.0f;
      y[i] = -3.5f;
      b[i] = 42.0f;
    }

    var sigma = new float[(w / 8) * (h / 8)];
    for (var i = 0; i < sigma.Length; i++) sigma[i] = 0.5f;
    var p = new EpfParams { Iters = 1, Sharpness = new float[8] };

    JxlEpf.Apply(new[] { x, y, b }, w, h, sigma, w / 8, h / 8, p);

    Assert.Multiple(() => {
      for (var i = 0; i < w * h; i++) {
        Assert.That(x[i], Is.EqualTo(7.0f).Within(1e-4f));
        Assert.That(y[i], Is.EqualTo(-3.5f).Within(1e-4f));
        Assert.That(b[i], Is.EqualTo(42.0f).Within(1e-4f));
      }
    });
  }

  // -----------------------------------------------------------------------
  // Apply — Iters > 1
  // -----------------------------------------------------------------------

  /// <summary>Iters=2 and Iters=3 are valid headers but unsupported in the
  /// first-wave Apply implementation — must throw NotImplementedException
  /// with a clear message rather than silently mis-filtering.</summary>
  [TestCase(2)]
  [TestCase(3)]
  public void Apply_ItersTwoOrThree_ThrowsNotImplemented(int iters) {
    const int w = 8, h = 8;
    var x = new float[w * h];
    var y = new float[w * h];
    var b = new float[w * h];
    var sigma = new float[1];
    var p = new EpfParams { Iters = iters, Sharpness = new float[8] };

    var ex = Assert.Throws<NotImplementedException>(
      () => JxlEpf.Apply(new[] { x, y, b }, w, h, sigma, 1, 1, p));
    Assert.That(ex!.Message, Does.Contain("EPF iters"));
  }

  // -----------------------------------------------------------------------
  // Apply — argument validation
  // -----------------------------------------------------------------------

  [Test]
  public void Apply_NullChannels_Throws() {
    var p = new EpfParams { Iters = 1, Sharpness = new float[8] };
    Assert.Throws<ArgumentNullException>(
      () => JxlEpf.Apply(null!, 8, 8, new float[1], 1, 1, p));
  }

  [Test]
  public void Apply_FewerThanThreeChannels_Throws() {
    var p = new EpfParams { Iters = 1, Sharpness = new float[8] };
    Assert.Throws<ArgumentException>(
      () => JxlEpf.Apply(new float[2][], 8, 8, new float[1], 1, 1, p));
  }

  [Test]
  public void Apply_NullParameters_Throws() {
    Assert.Throws<ArgumentNullException>(
      () => JxlEpf.Apply(new[] { new float[64], new float[64], new float[64] }, 8, 8, new float[1], 1, 1, null!));
  }

  [Test]
  public void Apply_ChannelTooShort_Throws() {
    var p = new EpfParams { Iters = 1, Sharpness = new float[8] };
    var channels = new[] { new float[16], new float[64], new float[64] };
    Assert.Throws<ArgumentException>(
      () => JxlEpf.Apply(channels, 8, 8, new float[1], 1, 1, p));
  }

  [Test]
  public void Apply_SigmaArrayTooSmall_Throws() {
    var p = new EpfParams { Iters = 1, Sharpness = new float[8] };
    var channels = new[] { new float[64], new float[64], new float[64] };
    Assert.Throws<ArgumentException>(
      () => JxlEpf.Apply(channels, 8, 8, new float[0], 1, 1, p));
  }

  // -----------------------------------------------------------------------
  // EpfParams data model
  // -----------------------------------------------------------------------

  [Test]
  public void EpfParams_Defaults_HaveExpectedShape() {
    var p = new EpfParams();
    Assert.Multiple(() => {
      Assert.That(p.Iters, Is.EqualTo(0));
      Assert.That(p.SigmaForModularX, Is.Empty);
      Assert.That(p.SigmaForModularY, Is.Empty);
      Assert.That(p.Sharpness, Is.Empty);
    });
  }

  // -----------------------------------------------------------------------
  // helpers
  // -----------------------------------------------------------------------

  private static float[] _MakeRamp(int n, float bias) {
    var a = new float[n];
    for (var i = 0; i < n; i++)
      a[i] = bias + i * 0.1f;
    return a;
  }
}
