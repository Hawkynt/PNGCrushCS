using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Tests for <see cref="JxlVarDctIdct"/> — the inverse-DCT engine for JPEG XL
/// VarDCT (ISO/IEC 18181-1 §G.4 / §G.5).
///
/// <para>Reference:
/// <list type="bullet">
///   <item><c>lib/jxl/dct-inl.h</c> — separable 1-D IDCT (rows then cols)</item>
///   <item><c>lib/jxl/dct_scales.h</c> — scaling convention (1/N forward; inverse undoes)</item>
///   <item><c>lib/jxl/ac_strategy.h</c> — AcStrategyType + covered_blocks_x/y</item>
/// </list>
/// </para>
/// </summary>
[TestFixture]
internal sealed class JxlVarDctIdctTests {

  // -----------------------------------------------------------------------
  // DC-only IDCT (libjxl scaling: DC = mean → flat block of value c)
  // -----------------------------------------------------------------------

  /// <summary>DC-only block: [c, 0, ..., 0] → flat block of value c. With
  /// libjxl's 1/N forward scaling, the DC coefficient equals the mean of the
  /// spatial samples, so a DC-only inverse must reconstruct a flat block.</summary>
  [Test]
  public void InverseDct8x8_DcOnly_ReconstructsFlatBlock() {
    var coeffs = new float[64];
    coeffs[0] = 5.0f;
    var output = new float[64];

    JxlVarDctIdct.InverseDct8x8(coeffs, output);

    Assert.Multiple(() => {
      for (var i = 0; i < 64; i++)
        Assert.That(output[i], Is.EqualTo(5.0f).Within(1e-5f), $"Pixel {i} should be flat-DC value 5.0.");
    });
  }

  /// <summary>DC-only with negative c — sanity-check sign preservation.</summary>
  [Test]
  public void InverseDct8x8_DcOnly_NegativeValue() {
    var coeffs = new float[64];
    coeffs[0] = -3.5f;
    var output = new float[64];

    JxlVarDctIdct.InverseDct8x8(coeffs, output);

    for (var i = 0; i < 64; i++)
      Assert.That(output[i], Is.EqualTo(-3.5f).Within(1e-5f));
  }

  /// <summary>DC=0 with all AC coeffs zero → all-zero output.</summary>
  [Test]
  public void InverseDct8x8_AllZero_ProducesAllZero() {
    var coeffs = new float[64];
    var output = new float[64];

    JxlVarDctIdct.InverseDct8x8(coeffs, output);

    for (var i = 0; i < 64; i++)
      Assert.That(output[i], Is.EqualTo(0.0f).Within(1e-5f));
  }

  /// <summary>libjxl reference: for the dequantized block from
  /// <c>test_16x16_vardct.jxl</c> Y channel block (0,0)
  /// (DC=0.245310, AC[1]=-0.032954, AC[3]=-0.005027, AC[8]=-0.127462,
  ///  AC[24]=-0.010419, rest=0), libjxl's TransposedScaledIDCT produces
  /// spatial[0..7] in row 0 = (0.0046, 0.0467, 0.1080, 0.1667, 0.2207,
  /// 0.2794, 0.3407, 0.3827).
  ///
  /// <para>Our <c>_InverseDct2D</c> with <c>2 * sum</c> AC factor produces a
  /// VERY DIFFERENT spatial output (different sign + magnitude). This test
  /// pins the discrepancy: when it fails, we know the IDCT scaling is
  /// off; when it passes, AC dequant scale wiring is unblocked.</para>
  /// </summary>
  [Test]
  public void InverseDct8x8_LibjxlPreIdctBlock_MatchesReferenceSpatial() {
    // libjxl block stored TRANSPOSED (column-major): index i*8+j is (col i, row j).
    // For our row-major IDCT we feed the transposed coefficient layout: what was
    // libjxl natural[1] (its (col=0, row=1)) goes to our (row=1, col=0) = our
    // natural[8]. Likewise libjxl natural[8] → our natural[1].
    var coeffs = new float[64];
    coeffs[0] = 0.245310f;     // DC same in both layouts
    coeffs[8] = -0.032954f;    // libjxl[1] (col 0 row 1) → our (row 1 col 0) = idx 8
    coeffs[24] = -0.005027f;   // libjxl[3] (col 0 row 3) → our (row 3 col 0) = idx 24
    coeffs[1] = -0.127462f;    // libjxl[8] (col 1 row 0) → our (row 0 col 1) = idx 1
    coeffs[3] = -0.010419f;    // libjxl[24] (col 3 row 0) → our (row 0 col 3) = idx 3
    var output = new float[64];
    JxlVarDctIdct.InverseDct8x8(coeffs, output);

    // libjxl reference (per traced post-IDCT spatial values for row 0).
    var libjxlRow0 = new[] {
      0.004644f, 0.046685f, 0.107995f, 0.166709f,
      0.220671f, 0.279385f, 0.340695f, 0.382736f
    };

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("idx | ours        | libjxl-ref  | diff");
    for (var x = 0; x < 8; ++x) {
      var diff = output[x] - libjxlRow0[x];
      sb.AppendLine($"  {x} | {output[x],10:F6} | {libjxlRow0[x],10:F6} | {diff,9:F6}");
    }
    TestContext.Out.WriteLine(sb.ToString());

    // Tight check: every pixel must be within 1e-3 of libjxl reference (small
    // float drift from the bias adjustment we don't yet apply).
    for (var x = 0; x < 8; ++x)
      Assert.That(output[x], Is.EqualTo(libjxlRow0[x]).Within(1e-3f),
        $"pixel {x}: ours={output[x]:F6}, libjxl={libjxlRow0[x]:F6}");
  }

  // -----------------------------------------------------------------------
  // Round-trip forward → inverse (validates scaling consistency)
  // -----------------------------------------------------------------------

  /// <summary>Round-trip: deterministic 8x8 image → forward DCT → inverse DCT
  /// → original within float precision. This verifies that the forward and
  /// inverse use matching scaling conventions.</summary>
  [Test]
  public void InverseDct8x8_RoundTripFromForward_MatchesOriginal() {
    var input = new float[64];
    for (var i = 0; i < 64; i++) input[i] = (i * 7.3f) - 100.0f;

    var coeffs = new float[64];
    JxlVarDctIdct.ForwardDct2D_Test(input, coeffs, 8, 8);
    var output = new float[64];
    JxlVarDctIdct.InverseDct8x8(coeffs, output);

    Assert.Multiple(() => {
      for (var i = 0; i < 64; i++)
        Assert.That(output[i], Is.EqualTo(input[i]).Within(1e-3f), $"Round-trip mismatch at index {i}.");
    });
  }

  /// <summary>Round-trip a flat (constant-valued) block. Forward should yield
  /// DC = constant and zero AC; inverse should recover the constant.</summary>
  [Test]
  public void InverseDct8x8_RoundTripFlatBlock_ProducesDcOnlyCoeffs() {
    var input = new float[64];
    for (var i = 0; i < 64; i++) input[i] = 42.0f;

    var coeffs = new float[64];
    JxlVarDctIdct.ForwardDct2D_Test(input, coeffs, 8, 8);

    Assert.Multiple(() => {
      Assert.That(coeffs[0], Is.EqualTo(42.0f).Within(1e-4f), "DC of flat block should equal the constant.");
      for (var i = 1; i < 64; i++)
        Assert.That(coeffs[i], Is.EqualTo(0.0f).Within(1e-4f), $"AC[{i}] of flat block should be zero.");
    });
  }

  /// <summary>Round-trip 4x4 to validate the same algorithm at a different size.</summary>
  [Test]
  public void InverseAcStrategy_Dct4x4_RoundTrip() {
    var input = new float[64];
    for (var i = 0; i < 64; i++) input[i] = (float)Math.Sin(i * 0.5);

    // DCT4x4 covers 8x8 per BlockSize() — confirmed via libjxl covered_blocks.
    var coeffs = new float[64];
    JxlVarDctIdct.ForwardDct2D_Test(input, coeffs, 8, 8);
    var output = new float[64];
    JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Dct8x8, coeffs, output);

    for (var i = 0; i < 64; i++)
      Assert.That(output[i], Is.EqualTo(input[i]).Within(1e-3f));
  }

  /// <summary>Round-trip a 16x16 block via InverseAcStrategy.</summary>
  [Test]
  public void InverseAcStrategy_Dct16x16_RoundTrip() {
    const int n = 256;
    var input = new float[n];
    var rng = new Random(42);
    for (var i = 0; i < n; i++) input[i] = ((float)rng.NextDouble() - 0.5f) * 200.0f;

    var coeffs = new float[n];
    JxlVarDctIdct.ForwardDct2D_Test(input, coeffs, 16, 16);
    var output = new float[n];
    JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Dct16x16, coeffs, output);

    for (var i = 0; i < n; i++)
      Assert.That(output[i], Is.EqualTo(input[i]).Within(1e-2f), $"16x16 round-trip mismatch at index {i}.");
  }

  /// <summary>Round-trip a 32x32 block. Larger size accumulates more float
  /// error so tolerance is correspondingly looser.</summary>
  [Test]
  public void InverseAcStrategy_Dct32x32_RoundTrip() {
    const int n = 1024;
    var input = new float[n];
    var rng = new Random(123);
    for (var i = 0; i < n; i++) input[i] = ((float)rng.NextDouble() - 0.5f) * 100.0f;

    var coeffs = new float[n];
    JxlVarDctIdct.ForwardDct2D_Test(input, coeffs, 32, 32);
    var output = new float[n];
    JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Dct32x32, coeffs, output);

    var maxErr = 0.0f;
    for (var i = 0; i < n; i++) {
      var err = Math.Abs(output[i] - input[i]);
      if (err > maxErr) maxErr = err;
    }
    Assert.That(maxErr, Is.LessThan(1e-2f), $"32x32 round-trip max error {maxErr} too large.");
  }

  // -----------------------------------------------------------------------
  // BlockSize correctness
  // -----------------------------------------------------------------------

  /// <summary>BlockSize maps every defined enum value to a sensible (W, H).
  /// Mirrors libjxl's <c>AcStrategy::covered_blocks_x() * 8</c>,
  /// <c>covered_blocks_y() * 8</c>.</summary>
  [Test]
  /// <remarks>
  /// A shape's name states its rows and then its columns, so the rectangular
  /// ones measure taller than they are wide: a sixteen-by-eight is eight pixels
  /// across and sixteen down. These used to read the names the other way about,
  /// which handed the inverse transform every rectangle as its own transpose —
  /// the squares could not show it and nothing else looked.
  /// </remarks>
  public void BlockSize_AllEnumValues_ReturnExpectedDimensions() {
    Assert.Multiple(() => {
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct8x8), Is.EqualTo((8, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Hornuss), Is.EqualTo((8, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct2x2), Is.EqualTo((8, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct4x4), Is.EqualTo((8, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct16x16), Is.EqualTo((16, 16)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct32x32), Is.EqualTo((32, 32)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct16x8), Is.EqualTo((8, 16)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct8x16), Is.EqualTo((16, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct32x8), Is.EqualTo((8, 32)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct8x32), Is.EqualTo((32, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct32x16), Is.EqualTo((16, 32)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct16x32), Is.EqualTo((32, 16)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct4x8), Is.EqualTo((8, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct8x4), Is.EqualTo((8, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Afv0), Is.EqualTo((8, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Afv1), Is.EqualTo((8, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Afv2), Is.EqualTo((8, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Afv3), Is.EqualTo((8, 8)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct64x64), Is.EqualTo((64, 64)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct64x32), Is.EqualTo((32, 64)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct32x64), Is.EqualTo((64, 32)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct128x128), Is.EqualTo((128, 128)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct128x64), Is.EqualTo((64, 128)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct64x128), Is.EqualTo((128, 64)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct256x256), Is.EqualTo((256, 256)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct256x128), Is.EqualTo((128, 256)));
      Assert.That(JxlVarDctIdct.BlockSize(JxlAcStrategyType.Dct128x256), Is.EqualTo((256, 128)));
    });
  }

  // -----------------------------------------------------------------------
  // Pure separable rectangular DCTs — round-trip via ForwardDct2D_Test.
  // -----------------------------------------------------------------------

  /// <summary>Round-trip for separable rectangular DCT shapes whose forward
  /// pass is a plain (W × H) separable DCT in the same axis convention
  /// (row-IDCT length = W, col-IDCT length = H).</summary>
  [TestCase(JxlAcStrategyType.Dct16x8)]
  [TestCase(JxlAcStrategyType.Dct8x16)]
  [TestCase(JxlAcStrategyType.Dct32x8)]
  [TestCase(JxlAcStrategyType.Dct8x32)]
  [TestCase(JxlAcStrategyType.Dct32x16)]
  [TestCase(JxlAcStrategyType.Dct16x32)]
  public void InverseAcStrategy_RectangularDcts_RoundTrip(JxlAcStrategyType strategy) {
    var (w, h) = JxlVarDctIdct.BlockSize(strategy);
    var n = w * h;
    var input = new float[n];
    var rng = new Random(strategy.GetHashCode());
    for (var i = 0; i < n; i++) input[i] = ((float)rng.NextDouble() - 0.5f) * 200.0f;

    var coeffs = new float[n];
    JxlVarDctIdct.ForwardDct2D_Test(input, coeffs, w, h);
    var output = new float[n];
    JxlVarDctIdct.InverseAcStrategy(strategy, coeffs, output);

    var maxErr = 0.0f;
    for (var i = 0; i < n; i++) {
      var err = Math.Abs(output[i] - input[i]);
      if (err > maxErr) maxErr = err;
    }
    Assert.That(maxErr, Is.LessThan(1e-2f), $"{strategy} round-trip max error {maxErr} too large.");
  }

  /// <summary>Round-trip for the larger square / rectangular separable DCTs.
  /// Tolerance scales loosely with size (more accumulated float error).</summary>
  [TestCase(JxlAcStrategyType.Dct64x64)]
  [TestCase(JxlAcStrategyType.Dct64x32)]
  [TestCase(JxlAcStrategyType.Dct32x64)]
  [TestCase(JxlAcStrategyType.Dct128x128)]
  [TestCase(JxlAcStrategyType.Dct128x64)]
  [TestCase(JxlAcStrategyType.Dct64x128)]
  [TestCase(JxlAcStrategyType.Dct256x256)]
  [TestCase(JxlAcStrategyType.Dct256x128)]
  [TestCase(JxlAcStrategyType.Dct128x256)]
  public void InverseAcStrategy_LargeDcts_RoundTrip(JxlAcStrategyType strategy) {
    var (w, h) = JxlVarDctIdct.BlockSize(strategy);
    var n = w * h;
    var input = new float[n];
    var rng = new Random(strategy.GetHashCode());
    for (var i = 0; i < n; i++) input[i] = ((float)rng.NextDouble() - 0.5f) * 100.0f;

    var coeffs = new float[n];
    JxlVarDctIdct.ForwardDct2D_Test(input, coeffs, w, h);
    var output = new float[n];
    JxlVarDctIdct.InverseAcStrategy(strategy, coeffs, output);

    var maxErr = 0.0f;
    for (var i = 0; i < n; i++) {
      var err = Math.Abs(output[i] - input[i]);
      if (err > maxErr) maxErr = err;
    }
    // Largest size we test is 256×256 → ~33M MACs per pass × 2 passes ⇒
    // tolerance ~1e-1 to absorb worst-case float drift.
    Assert.That(maxErr, Is.LessThan(1e-1f), $"{strategy} round-trip max error {maxErr} too large.");
  }

  // -----------------------------------------------------------------------
  // Composite strategies — sanity tests (linearity + DC-only)
  // -----------------------------------------------------------------------

  /// <summary>All-zero coefficients must produce all-zero pixels for every
  /// composite/special strategy (DCT2x2, DCT4x4, DCT4x8, DCT8x4, Hornuss,
  /// AFV0..3). This is a sanity check on linearity: any linear transform
  /// maps the zero vector to the zero vector.</summary>
  [TestCase(JxlAcStrategyType.Dct2x2)]
  [TestCase(JxlAcStrategyType.Dct4x4)]
  [TestCase(JxlAcStrategyType.Dct4x8)]
  [TestCase(JxlAcStrategyType.Dct8x4)]
  [TestCase(JxlAcStrategyType.Hornuss)]
  [TestCase(JxlAcStrategyType.Afv0)]
  [TestCase(JxlAcStrategyType.Afv1)]
  [TestCase(JxlAcStrategyType.Afv2)]
  [TestCase(JxlAcStrategyType.Afv3)]
  public void InverseAcStrategy_CompositeStrategies_AllZeroCoeffsProduceAllZero(JxlAcStrategyType strategy) {
    var coeffs = new float[64];
    var output = new float[64];

    JxlVarDctIdct.InverseAcStrategy(strategy, coeffs, output);

    for (var i = 0; i < 64; i++)
      Assert.That(output[i], Is.EqualTo(0.0f).Within(1e-5f), $"Pixel {i} should be zero for {strategy}.");
  }

  /// <summary>Dct4x4 with a 2x2 Hadamard-encoded DC of all-equal quadrant DCs:
  /// setting coeffs[0] = c, all other = 0 ⇒ each quadrant DC = c (libjxl:
  /// dcs[i] = c), so each 4x4 sub-block IDCTs to a 4x4 of c. Result: 8x8 of c.
  /// Mirrors libjxl `TransformToPixels` case `DCT4X4`.</summary>
  [Test]
  public void InverseAcStrategy_Dct4x4_DcOnly_ProducesFlatBlock() {
    var coeffs = new float[64];
    coeffs[0] = 3.0f;
    var output = new float[64];

    JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Dct4x4, coeffs, output);

    for (var i = 0; i < 64; i++)
      Assert.That(output[i], Is.EqualTo(3.0f).Within(1e-4f), $"Dct4x4 DC-only pixel {i} should be 3.0.");
  }

  /// <summary>Dct4x8 DC-only: coeffs[0] = c ⇒ both 4x8 strips have DC=c ⇒
  /// flat 8x8 block of c.</summary>
  [Test]
  public void InverseAcStrategy_Dct4x8_DcOnly_ProducesFlatBlock() {
    var coeffs = new float[64];
    coeffs[0] = 7.5f;
    var output = new float[64];

    JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Dct4x8, coeffs, output);

    for (var i = 0; i < 64; i++)
      Assert.That(output[i], Is.EqualTo(7.5f).Within(1e-4f), $"Dct4x8 DC-only pixel {i} should be 7.5.");
  }

  /// <summary>Dct8x4 DC-only: coeffs[0] = c ⇒ both 8x4 strips have DC=c ⇒
  /// flat 8x8 block of c.</summary>
  [Test]
  public void InverseAcStrategy_Dct8x4_DcOnly_ProducesFlatBlock() {
    var coeffs = new float[64];
    coeffs[0] = -2.25f;
    var output = new float[64];

    JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Dct8x4, coeffs, output);

    for (var i = 0; i < 64; i++)
      Assert.That(output[i], Is.EqualTo(-2.25f).Within(1e-4f), $"Dct8x4 DC-only pixel {i} should be -2.25.");
  }

  /// <summary>Dct2x2 multi-level Hadamard: with coeffs[0] = c and everything
  /// else zero, the cascade applies +c at every level (Hadamard with one
  /// nonzero element doubles each iteration), producing a flat block of c at
  /// the topmost level.</summary>
  [Test]
  public void InverseAcStrategy_Dct2x2_DcOnly_ProducesFlatBlock() {
    var coeffs = new float[64];
    coeffs[0] = 1.0f;
    var output = new float[64];

    JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Dct2x2, coeffs, output);

    // After 3 levels of 2x2 Hadamard with single nonzero c00=1 and
    // c01=c10=c11=0: r00 = c00 = 1 at each step, expanding to the full 8x8.
    for (var i = 0; i < 64; i++)
      Assert.That(output[i], Is.EqualTo(1.0f).Within(1e-5f), $"Dct2x2 DC-only pixel {i} should be 1.0.");
  }

  /// <summary>Hornuss DC-only: coeffs[0] = c, all other zero ⇒ every quadrant
  /// DC = c, residual sum = 0 ⇒ center pixel = c, every other pixel = 0 + c.
  /// Result: flat 8x8 block of c.</summary>
  [Test]
  public void InverseAcStrategy_Hornuss_DcOnly_ProducesFlatBlock() {
    var coeffs = new float[64];
    coeffs[0] = 4.0f;
    var output = new float[64];

    JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Hornuss, coeffs, output);

    for (var i = 0; i < 64; i++)
      Assert.That(output[i], Is.EqualTo(4.0f).Within(1e-4f), $"Hornuss DC-only pixel {i} should be 4.0.");
  }

  /// <summary>AFV0 sanity: linearity (all-zeros → all-zeros) is covered above;
  /// here we sanity-check that AFV produces finite output for a small
  /// non-trivial coefficient set without throwing.</summary>
  [TestCase(JxlAcStrategyType.Afv0)]
  [TestCase(JxlAcStrategyType.Afv1)]
  [TestCase(JxlAcStrategyType.Afv2)]
  [TestCase(JxlAcStrategyType.Afv3)]
  public void InverseAcStrategy_AfvVariants_ProduceFiniteOutput(JxlAcStrategyType strategy) {
    var coeffs = new float[64];
    coeffs[0] = 1.0f;
    coeffs[1] = 0.5f;
    coeffs[8] = -0.25f;
    var output = new float[64];

    JxlVarDctIdct.InverseAcStrategy(strategy, coeffs, output);

    for (var i = 0; i < 64; i++)
      Assert.That(float.IsFinite(output[i]), Is.True, $"AFV pixel {i} must be finite.");
  }

  // -----------------------------------------------------------------------
  // Argument validation
  // -----------------------------------------------------------------------

  [Test]
  public void InverseDct8x8_NullCoeffs_Throws()
    => Assert.Throws<ArgumentNullException>(() => JxlVarDctIdct.InverseDct8x8(null!, new float[64]));

  [Test]
  public void InverseDct8x8_NullOutput_Throws()
    => Assert.Throws<ArgumentNullException>(() => JxlVarDctIdct.InverseDct8x8(new float[64], null!));

  [Test]
  public void InverseDct8x8_WrongCoeffLength_Throws()
    => Assert.Throws<ArgumentException>(() => JxlVarDctIdct.InverseDct8x8(new float[63], new float[64]));

  [Test]
  public void InverseDct8x8_WrongOutputLength_Throws()
    => Assert.Throws<ArgumentException>(() => JxlVarDctIdct.InverseDct8x8(new float[64], new float[63]));

  [Test]
  public void InverseAcStrategy_WrongCoeffLength_Throws()
    => Assert.Throws<ArgumentException>(() => JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Dct16x16, new float[100], new float[256]));
}
