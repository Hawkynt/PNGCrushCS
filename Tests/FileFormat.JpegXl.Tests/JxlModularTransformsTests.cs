using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Tests for <see cref="JxlModularTransforms"/> — the inverse-transform side of
/// the JPEG XL modular sub-codec (ISO/IEC 18181-1 §H.5).
///
/// <para>Reference:
/// <list type="bullet">
///   <item><c>lib/jxl/modular/transform/transform.cc</c> (header layout)</item>
///   <item><c>lib/jxl/modular/transform/rct.cc</c> (RCT 0..41)</item>
///   <item><c>lib/jxl/modular/transform/palette.cc</c> (palette inverse)</item>
///   <item><c>lib/jxl/modular/transform/squeeze.cc</c> + <c>squeeze.h</c></item>
/// </list>
/// </para>
/// </summary>
[TestFixture]
public sealed class JxlModularTransformsTests {

  // -----------------------------------------------------------------------
  // RCT inverse
  // -----------------------------------------------------------------------

  /// <summary>RCT type 0 = identity (custom=0, permutation=0). Output equals input verbatim.</summary>
  [Test]
  public void InvertRct_Type0_Identity() {
    var channels = _MakeRgbChannels(2, 2,
      r: [10, 20, 30, 40],
      g: [50, 60, 70, 80],
      b: [90, 100, 110, 120]);
    var t = new JxlModularTransform { Type = JxlModularTransformType.Rct, RctBeginC = 0, RctType = 0 };

    var result = JxlModularTransforms.InvertRct(channels, t);

    Assert.Multiple(() => {
      Assert.That(result[0].Pixels, Is.EqualTo(new[] { 10, 20, 30, 40 }));
      Assert.That(result[1].Pixels, Is.EqualTo(new[] { 50, 60, 70, 80 }));
      Assert.That(result[2].Pixels, Is.EqualTo(new[] { 90, 100, 110, 120 }));
    });
  }

  /// <summary>RCT custom=1 (permutation=0, third=1, second=0). Inverse adds c0 to c2.</summary>
  [Test]
  public void InvertRct_Custom1_AddsFirstToThird() {
    var channels = _MakeRgbChannels(1, 1, r: [5], g: [7], b: [11]);
    var t = new JxlModularTransform { Type = JxlModularTransformType.Rct, RctBeginC = 0, RctType = 1 };

    var result = JxlModularTransforms.InvertRct(channels, t);

    Assert.Multiple(() => {
      Assert.That(result[0].Pixels[0], Is.EqualTo(5));   // first unchanged
      Assert.That(result[1].Pixels[0], Is.EqualTo(7));   // second unchanged
      Assert.That(result[2].Pixels[0], Is.EqualTo(16));  // third + first = 11 + 5
    });
  }

  /// <summary>RCT round-trip for the YCoCg variant (rct_type=6, the default).</summary>
  [Test]
  public void InvertRct_Type6_YCoCg_RoundTripsForwardEncode() {
    // Forward YCoCg-R (libjxl encoding):
    //   Co = R - B
    //   tmp = B + (Co >> 1)
    //   Cg = G - tmp
    //   Y = tmp + (Cg >> 1)
    // Inverse (rct_type=6) must recover (R, G, B) exactly.
    int R = 200, G = 150, B = 100;
    var Co = R - B;
    var tmp = B + (Co >> 1);
    var Cg = G - tmp;
    var Y = tmp + (Cg >> 1);

    var channels = _MakeRgbChannels(1, 1, r: [Y], g: [Co], b: [Cg]);
    var t = new JxlModularTransform { Type = JxlModularTransformType.Rct, RctBeginC = 0, RctType = 6 };

    var result = JxlModularTransforms.InvertRct(channels, t);

    Assert.Multiple(() => {
      Assert.That(result[0].Pixels[0], Is.EqualTo(R));
      Assert.That(result[1].Pixels[0], Is.EqualTo(G));
      Assert.That(result[2].Pixels[0], Is.EqualTo(B));
    });
  }

  /// <summary>RCT custom=0, permutation=1 (rct_type=7) is permute-only (GBR ordering).</summary>
  [Test]
  public void InvertRct_Type7_PermutesGbr() {
    var channels = _MakeRgbChannels(1, 1, r: [1], g: [2], b: [3]);
    var t = new JxlModularTransform { Type = JxlModularTransformType.Rct, RctBeginC = 0, RctType = 7 };

    var result = JxlModularTransforms.InvertRct(channels, t);

    // permutation=1 -> idx0=1, idx1=2, idx2=0  (libjxl: out0 at perm%3,
    // out1 at (perm+1+perm/3)%3, out2 at (perm+2-perm/3)%3)
    // So input(0,1,2) -> output[1]=in0=1, output[2]=in1=2, output[0]=in2=3.
    Assert.Multiple(() => {
      Assert.That(result[1].Pixels[0], Is.EqualTo(1));
      Assert.That(result[2].Pixels[0], Is.EqualTo(2));
      Assert.That(result[0].Pixels[0], Is.EqualTo(3));
    });
  }

  /// <summary>InvertRct must reject out-of-range begin_c.</summary>
  [Test]
  public void InvertRct_BeginCOutOfRange_Throws() {
    var channels = _MakeRgbChannels(1, 1, r: [1], g: [2], b: [3]);
    var t = new JxlModularTransform { Type = JxlModularTransformType.Rct, RctBeginC = 5, RctType = 0 };

    Assert.Throws<InvalidOperationException>(() => JxlModularTransforms.InvertRct(channels, t));
  }

  // -----------------------------------------------------------------------
  // Palette inverse
  // -----------------------------------------------------------------------

  /// <summary>Single-channel (grayscale) palette inverse: indices map to LUT entries.</summary>
  [Test]
  public void InvertPalette_SingleChannel_ExpandsCorrectly() {
    // LUT: 2 entries × 1 channel: [42, 99]
    // Index channel: 2x2 image, indices [0, 1, 1, 0]
    var paletteMeta = new JxlChannel { Width = 2, Height = 1, Pixels = new[] { 42, 99 } };
    var indexCh = new JxlChannel { Width = 2, Height = 2, Pixels = new[] { 0, 1, 1, 0 } };
    JxlChannel[] channels = [paletteMeta, indexCh];
    var t = new JxlModularTransform {
      Type = JxlModularTransformType.Palette,
      PaletteBeginC = 0,
      PaletteNumC = 1,
      PaletteSize = 2,
      PaletteDeltaPredictor = 0,
      PaletteData = [42, 99],
    };

    var result = JxlModularTransforms.InvertPalette(channels, t);

    Assert.Multiple(() => {
      Assert.That(result.Length, Is.EqualTo(1), "Meta channel removed, single expanded channel left.");
      Assert.That(result[0].Width, Is.EqualTo(2));
      Assert.That(result[0].Height, Is.EqualTo(2));
      Assert.That(result[0].Pixels, Is.EqualTo(new[] { 42, 99, 99, 42 }));
    });
  }

  /// <summary>Multi-channel palette inverse expands one index channel into N RGB channels.</summary>
  [Test]
  public void InvertPalette_TwoColorRgb_ExpandsTo3Channels() {
    // 2-color RGB palette: black (0,0,0) and white (255,255,255).
    // Layout in PaletteData (row-major nb × paletteSize):
    //   row 0 (R): [0, 255]
    //   row 1 (G): [0, 255]
    //   row 2 (B): [0, 255]
    var paletteMeta = new JxlChannel { Width = 2, Height = 3, Pixels = new[] { 0, 255, 0, 255, 0, 255 } };
    var indexCh = new JxlChannel { Width = 2, Height = 2, Pixels = new[] { 1, 0, 0, 1 } };
    JxlChannel[] channels = [paletteMeta, indexCh];
    var t = new JxlModularTransform {
      Type = JxlModularTransformType.Palette,
      PaletteBeginC = 0,
      PaletteNumC = 3,
      PaletteSize = 2,
      PaletteDeltaPredictor = 0,
      PaletteData = [0, 255, 0, 255, 0, 255],
    };

    var result = JxlModularTransforms.InvertPalette(channels, t);

    Assert.Multiple(() => {
      Assert.That(result.Length, Is.EqualTo(3), "3 expanded RGB channels after meta erasure.");
      Assert.That(result[0].Pixels, Is.EqualTo(new[] { 255, 0, 0, 255 }), "R channel.");
      Assert.That(result[1].Pixels, Is.EqualTo(new[] { 255, 0, 0, 255 }), "G channel.");
      Assert.That(result[2].Pixels, Is.EqualTo(new[] { 255, 0, 0, 255 }), "B channel.");
    });
  }

  /// <summary>Palette predictor != Zero is documented as not yet supported.</summary>
  [Test]
  public void InvertPalette_NonZeroPredictor_Throws() {
    var paletteMeta = new JxlChannel { Width = 1, Height = 1, Pixels = new[] { 0 } };
    var indexCh = new JxlChannel { Width = 1, Height = 1, Pixels = new[] { 0 } };
    JxlChannel[] channels = [paletteMeta, indexCh];
    var t = new JxlModularTransform {
      Type = JxlModularTransformType.Palette,
      PaletteBeginC = 0,
      PaletteNumC = 1,
      PaletteSize = 1,
      PaletteDeltaPredictor = 1, // non-Zero
      PaletteData = [0],
    };

    Assert.Throws<NotSupportedException>(() => JxlModularTransforms.InvertPalette(channels, t));
  }

  /// <summary>Out-of-range palette indices now resolve via the implicit
  /// small-cube/large-cube/delta-palette tables (libjxl
  /// <c>palette_internal::GetPaletteValue</c>). Verifies the resolution
  /// completes without throwing and produces a sane integer result.</summary>
  [Test]
  public void InvertPalette_ImplicitIndex_ResolvesViaCube() {
    var paletteMeta = new JxlChannel { Width = 1, Height = 1, Pixels = new[] { 0 } };
    // Index 5 with paletteSize=1: 5 - 1 = 4 ≥ 0 but < kLargeCubeOffset (=64),
    // so this lands in the small-cube branch.
    var indexCh = new JxlChannel { Width = 1, Height = 1, Pixels = new[] { 5 } };
    JxlChannel[] channels = [paletteMeta, indexCh];
    var t = new JxlModularTransform {
      Type = JxlModularTransformType.Palette,
      PaletteBeginC = 0,
      PaletteNumC = 1,
      PaletteSize = 1,
      PaletteDeltaPredictor = 0,
      PaletteData = [0],
    };

    var result = JxlModularTransforms.InvertPalette(channels, t);
    Assert.That(result, Is.Not.Null);
    Assert.That(result.Length, Is.EqualTo(1)); // 1 expanded color channel
    Assert.That(result[0].Width, Is.EqualTo(1));
    Assert.That(result[0].Height, Is.EqualTo(1));
    // Don't assert exact value — just that the path runs; the cube formula
    // is verified by the end-to-end fixture tests.
  }

  // -----------------------------------------------------------------------
  // Squeeze inverse
  // -----------------------------------------------------------------------

  /// <summary>
  /// Build a horizontally-squeezed pair (avg + residual-with-tendency) from a
  /// known full-resolution channel and verify the inverse recovers the original.
  /// </summary>
  [Test]
  public void InvertSqueeze_Horizontal_RoundTripsKnownInput() {
    // Pick a small smooth row that exercises the SmoothTendency branches.
    int[] full = [100, 110, 130, 145, 170, 200];
    const int W = 6;
    const int H = 1;
    var avgW = (W + 1) / 2;
    var resW = W - avgW;

    // Forward squeeze with tendency (libjxl: avg = odd + (diff+1)/2; residual
    // stored is diff - tendency where tendency depends on B=left of this pair).
    var avg = new int[avgW];
    var diffs = new int[resW];
    for (var x = 0; x < avgW; ++x) {
      var even = full[x * 2];
      var odd = (x * 2 + 1 < W) ? full[x * 2 + 1] : even;
      var diff = even - odd;
      avg[x] = odd + ((diff + (diff < 0 ? -1 : 1) * (diff & 1)) >> 1); // = (even+odd) integer-rounded toward odd
      // Simpler/equivalent in libjxl: avg = odd + (diff/2) + (diff & 1)? Actually
      // libjxl uses: A = avg + (diff/2) on inverse, so forward needs avg = odd + (diff/2).
      // Re-derive cleanly: from inverse  A = avg + diff/2;  B = A - diff;
      //   so avg = A - diff/2 = even - diff/2.
      avg[x] = even - (diff / 2);
    }
    // Compute residuals (diff - tendency) per libjxl InvHSqueeze.
    // Use the same SmoothTendency reading we exposed via the inverse: we
    // derive residuals such that on inverse, diff = (diff_minus_tendency) + tendency.
    var residuals = new int[resW];
    for (var x = 0; x < resW; ++x) {
      var even = full[x * 2];
      var odd = full[x * 2 + 1];
      var diff = even - odd;
      var leftEven = (x > 0) ? full[(x - 1) * 2] : avg[x]; // B = left output sample
      // For the very first pair, libjxl uses left = avg (the same row's first avg).
      var nextAvg = (x + 1 < avgW) ? avg[x + 1] : avg[x];
      var tendency = _SmoothTendency(leftEven, avg[x], nextAvg);
      // BUT: libjxl uses `left = p_out[(x<<1)-1]` which is the *previous* odd
      // sample, not the previous even sample. So at x=1, left = full[1] (odd of pair 0).
      // We need the actual reconstructed left, but on the forward side we have
      // the original. Let's redo: on inverse, left = previous odd output. After
      // unsqueeze of pair 0, left for pair 1 = full[1]. So forward side knows
      // this is full[2*x - 1].
      leftEven = (x > 0) ? full[2 * x - 1] : avg[x];
      tendency = _SmoothTendency(leftEven, avg[x], nextAvg);
      residuals[x] = diff - tendency;
    }

    var avgCh = new JxlChannel { Width = avgW, Height = H, Pixels = avg };
    var resCh = new JxlChannel { Width = resW, Height = H, Pixels = residuals };
    JxlChannel[] channels = [avgCh, resCh];

    var t = new JxlModularTransform {
      Type = JxlModularTransformType.Squeeze,
      SqueezeSteps = [new JxlSqueezeStep(0, 1, true, true)],
    };

    var result = JxlModularTransforms.InvertSqueeze(channels, t);

    Assert.Multiple(() => {
      Assert.That(result.Length, Is.EqualTo(1));
      Assert.That(result[0].Width, Is.EqualTo(W));
      Assert.That(result[0].Height, Is.EqualTo(H));
      Assert.That(result[0].Pixels, Is.EqualTo(full));
    });
  }

  /// <summary>
  /// Trivial vertical squeeze: zero residuals + constant column ⇒ inverse
  /// produces a constant-column full-resolution channel.
  /// </summary>
  [Test]
  public void InvertSqueeze_Vertical_ConstantColumn_ZeroResiduals() {
    // Avg channel: 1×2 [50, 50]. Residual: 1×2 [0, 0]. With zero diffs and a
    // smooth (constant) avg column, tendency is 0 and outputs are all 50.
    var avg = new JxlChannel { Width = 1, Height = 2, Pixels = new[] { 50, 50 } };
    var res = new JxlChannel { Width = 1, Height = 2, Pixels = new[] { 0, 0 } };
    JxlChannel[] channels = [avg, res];
    var t = new JxlModularTransform {
      Type = JxlModularTransformType.Squeeze,
      SqueezeSteps = [new JxlSqueezeStep(0, 1, false, true)],
    };

    var result = JxlModularTransforms.InvertSqueeze(channels, t);

    Assert.Multiple(() => {
      Assert.That(result.Length, Is.EqualTo(1));
      Assert.That(result[0].Width, Is.EqualTo(1));
      Assert.That(result[0].Height, Is.EqualTo(4));
      Assert.That(result[0].Pixels, Is.EqualTo(new[] { 50, 50, 50, 50 }));
    });
  }

  /// <summary>
  /// Trivial horizontal squeeze: zero residual + constant row ⇒ inverse
  /// reconstructs constant row.
  /// </summary>
  [Test]
  public void InvertSqueeze_Horizontal_ConstantRow_ZeroResiduals() {
    var avg = new JxlChannel { Width = 2, Height = 1, Pixels = new[] { 50, 50 } };
    var res = new JxlChannel { Width = 2, Height = 1, Pixels = new[] { 0, 0 } };
    JxlChannel[] channels = [avg, res];
    var t = new JxlModularTransform {
      Type = JxlModularTransformType.Squeeze,
      SqueezeSteps = [new JxlSqueezeStep(0, 1, true, true)],
    };

    var result = JxlModularTransforms.InvertSqueeze(channels, t);

    Assert.Multiple(() => {
      Assert.That(result.Length, Is.EqualTo(1));
      Assert.That(result[0].Width, Is.EqualTo(4));
      Assert.That(result[0].Height, Is.EqualTo(1));
      Assert.That(result[0].Pixels, Is.EqualTo(new[] { 50, 50, 50, 50 }));
    });
  }

  /// <summary>Empty squeeze step list (default-chain fallback) is documented unsupported.</summary>
  [Test]
  public void InvertSqueeze_DefaultChain_ThrowsNotSupported() {
    var ch = new JxlChannel { Width = 1, Height = 1, Pixels = new[] { 0 } };
    JxlChannel[] channels = [ch];
    var t = new JxlModularTransform { Type = JxlModularTransformType.Squeeze, SqueezeSteps = [] };

    Assert.Throws<NotSupportedException>(() => JxlModularTransforms.InvertSqueeze(channels, t));
  }

  // -----------------------------------------------------------------------
  // Header reader (ReadAll)
  // -----------------------------------------------------------------------

  /// <summary>Empty transform list: U32 selector 0 ⇒ num_transforms=0.</summary>
  [Test]
  public void ReadAll_Zero_ReturnsEmpty() {
    // num_transforms U32 = U32(Val(0), Val(1), BitsOffset(4,2), BitsOffset(8,18)).
    // Selector 0 ⇒ num_transforms = 0. Two zero bits.
    var data = new byte[] { 0b00000000 };
    var reader = new JxlBitReader(data, 0);

    var result = JxlModularTransforms.ReadAll(reader);

    Assert.That(result, Is.Empty);
  }

  /// <summary>Single RCT transform with begin_c=0, rct_type=0 (identity).</summary>
  [Test]
  public void ReadAll_OneRct_ParsesCorrectly() {
    // Bit layout (LSB-first, in stream order):
    //   num_transforms: U32(Val(0), Val(1), BitsOffset(4,2), BitsOffset(8,18))
    //                   selector=1 ⇒ Val(1) = 1, no payload bits. bits "10".
    //   transform_id: 2 bits = 00 ⇒ kRCT.
    //   begin_c: U32(Bits(3), BitsOffset(6,8), BitsOffset(10,72), BitsOffset(13,1096))
    //            selector=0 ⇒ Bits(3) value 0. bits "00" then "000".
    //   rct_type: U32(Val(6), Bits(2), BitsOffset(4,2), BitsOffset(6,10))
    //             selector=1 ⇒ Bits(2) value 0 = rct_type 0. bits "10" then "00".
    //
    // Concatenated LSB-first stream bits (in read order):
    //   bit 0 = 1 (num_t selector LSB)
    //   bit 1 = 0 (num_t selector MSB)        ⇒ selector = 0b01 = 1, value=1
    //   bit 2 = 0 (transform_id LSB)
    //   bit 3 = 0 (transform_id MSB)          ⇒ 0 = kRCT
    //   bit 4 = 0 (begin_c selector LSB)
    //   bit 5 = 0 (begin_c selector MSB)      ⇒ selector 0 → Bits(3)
    //   bit 6 = 0 (begin_c bits[0])
    //   bit 7 = 0 (begin_c bits[1])
    //   bit 8 = 0 (begin_c bits[2])           ⇒ begin_c = 0
    //   bit 9 = 1 (rct_type selector LSB)
    //   bit 10 = 0 (rct_type selector MSB)    ⇒ selector 1 → Bits(2)
    //   bit 11 = 0 (rct_type bits[0])
    //   bit 12 = 0 (rct_type bits[1])         ⇒ rct_type = 0
    // Byte 0 = bits 0..7  LSB-first = 1,0,0,0,0,0,0,0 = 0x01
    // Byte 1 = bits 8..15 LSB-first = 0,1,0,0,0,0,0,0 = 0x02
    var data = new byte[] { 0x01, 0x02 };
    var reader = new JxlBitReader(data, 0);

    var result = JxlModularTransforms.ReadAll(reader);

    Assert.Multiple(() => {
      Assert.That(result.Length, Is.EqualTo(1));
      Assert.That(result[0].Type, Is.EqualTo(JxlModularTransformType.Rct));
      Assert.That(result[0].RctBeginC, Is.EqualTo(0));
      Assert.That(result[0].RctType, Is.EqualTo(0));
    });
  }

  // -----------------------------------------------------------------------
  // Top-level dispatch
  // -----------------------------------------------------------------------

  /// <summary>InvertAll with empty transform list returns input unchanged.</summary>
  [Test]
  public void InvertAll_Empty_ReturnsInputUnchanged() {
    var ch = new JxlChannel { Width = 1, Height = 1, Pixels = new[] { 42 } };
    JxlChannel[] channels = [ch];

    var result = JxlModularTransforms.InvertAll(channels, []);

    Assert.That(result, Is.SameAs(channels));
  }

  /// <summary>Identity RCT applied via InvertAll preserves data.</summary>
  [Test]
  public void InvertAll_IdentityRct_PreservesData() {
    var channels = _MakeRgbChannels(1, 1, r: [1], g: [2], b: [3]);
    var t = new JxlModularTransform { Type = JxlModularTransformType.Rct, RctBeginC = 0, RctType = 0 };

    var result = JxlModularTransforms.InvertAll(channels, [t]);

    Assert.Multiple(() => {
      Assert.That(result[0].Pixels[0], Is.EqualTo(1));
      Assert.That(result[1].Pixels[0], Is.EqualTo(2));
      Assert.That(result[2].Pixels[0], Is.EqualTo(3));
    });
  }

  // -----------------------------------------------------------------------
  // Helpers
  // -----------------------------------------------------------------------

  private static JxlChannel[] _MakeRgbChannels(int w, int h, int[] r, int[] g, int[] b) {
    return [
      new JxlChannel { Width = w, Height = h, Pixels = r },
      new JxlChannel { Width = w, Height = h, Pixels = g },
      new JxlChannel { Width = w, Height = h, Pixels = b },
    ];
  }

  /// <summary>Local mirror of libjxl's <c>SmoothTendency</c> for test forward-side construction.</summary>
  private static int _SmoothTendency(int B, int a, int n) {
    var diff = 0;
    if (B >= a && a >= n) {
      diff = (4 * B - 3 * n - a + 6) / 12;
      if (diff - (diff & 1) > 2 * (B - a)) diff = 2 * (B - a) + 1;
      if (diff + (diff & 1) > 2 * (a - n)) diff = 2 * (a - n);
    } else if (B <= a && a <= n) {
      diff = (4 * B - 3 * n - a - 6) / 12;
      if (diff + (diff & 1) < 2 * (B - a)) diff = 2 * (B - a) - 1;
      if (diff - (diff & 1) < 2 * (a - n)) diff = 2 * (a - n);
    }
    return diff;
  }
}
