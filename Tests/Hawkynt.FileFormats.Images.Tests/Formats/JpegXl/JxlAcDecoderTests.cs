using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Tests for <see cref="JxlAcDecoder"/> (ISO/IEC 18181-1 §G.7 / libjxl
/// <c>DecodeACVarBlock</c> in <c>lib/jxl/dec_group.cc</c>).
///
/// <para>The decoder takes a per-block AC-strategy grid (currently DCT8-only)
/// and produces per-channel arrays of 64-element scan-order coefficient
/// blocks. DC (index 0) is filled by the LF stream — the AC stream produces
/// only the high-frequency 1..63 positions per block.</para>
/// </summary>
[TestFixture]
internal sealed class JxlAcDecoderTests {

  // ============================================================
  // Argument validation
  // ============================================================

  /// <summary>Null reader is rejected.</summary>
  [Test]
  public void DecodeGroup_NullReader_Throws() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(2, 2);
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    Assert.Throws<ArgumentNullException>(
      () => JxlAcDecoder.DecodeGroup(null!, entropy, strategies, bcm, 2, 2, 3));
  }

  /// <summary>Null entropy decoder is rejected.</summary>
  [Test]
  public void DecodeGroup_NullEntropy_Throws() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(2, 2);
    var reader = new JxlBitReader(bytes, 0);

    Assert.Throws<ArgumentNullException>(
      () => JxlAcDecoder.DecodeGroup(reader, null!, strategies, bcm, 2, 2, 3));
  }

  /// <summary>Null strategies grid is rejected.</summary>
  [Test]
  public void DecodeGroup_NullStrategies_Throws() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    Assert.Throws<ArgumentNullException>(
      () => JxlAcDecoder.DecodeGroup(reader, entropy, null!, bcm, 2, 2, 3));
  }

  /// <summary>Null context map is rejected.</summary>
  [Test]
  public void DecodeGroup_NullContextMap_Throws() {
    var bytes = new byte[16];
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(2, 2);
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    Assert.Throws<ArgumentNullException>(
      () => JxlAcDecoder.DecodeGroup(reader, entropy, strategies, null!, 2, 2, 3));
  }

  /// <summary>Negative width is rejected.</summary>
  [Test]
  public void DecodeGroup_NegativeWidth_Throws() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    var strategies = Array.Empty<JxlAcStrategyType[]>();
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    Assert.Throws<ArgumentOutOfRangeException>(
      () => JxlAcDecoder.DecodeGroup(reader, entropy, strategies, bcm, -1, 0, 3));
  }

  /// <summary>Negative height is rejected.</summary>
  [Test]
  public void DecodeGroup_NegativeHeight_Throws() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    var strategies = Array.Empty<JxlAcStrategyType[]>();
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    Assert.Throws<ArgumentOutOfRangeException>(
      () => JxlAcDecoder.DecodeGroup(reader, entropy, strategies, bcm, 0, -1, 3));
  }

  /// <summary>Zero numChannels is rejected.</summary>
  [Test]
  public void DecodeGroup_ZeroChannels_Throws() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(1, 1);
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    Assert.Throws<ArgumentOutOfRangeException>(
      () => JxlAcDecoder.DecodeGroup(reader, entropy, strategies, bcm, 1, 1, 0));
  }

  /// <summary>Strategies grid with the wrong row count is rejected.</summary>
  [Test]
  public void DecodeGroup_StrategiesWrongRowCount_Throws() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    // Strategies says 4 high but we ask for 2.
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(2, 4);
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    Assert.Throws<ArgumentException>(
      () => JxlAcDecoder.DecodeGroup(reader, entropy, strategies, bcm, 2, 2, 3));
  }

  /// <summary>Strategies grid with the wrong column count is rejected.</summary>
  [Test]
  public void DecodeGroup_StrategiesWrongColumnCount_Throws() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    // 4-wide grid but we say 2.
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(4, 2);
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    Assert.Throws<ArgumentException>(
      () => JxlAcDecoder.DecodeGroup(reader, entropy, strategies, bcm, 2, 2, 3));
  }

  // ============================================================
  // Empty / zero-size groups
  // ============================================================

  /// <summary>Zero-block group returns per-channel arrays of length zero
  /// without consuming any bits or invoking the entropy decoder.</summary>
  [Test]
  public void DecodeGroup_ZeroBlocks_ReturnsEmptyChannels() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(0, 0);
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    var result = JxlAcDecoder.DecodeGroup(reader, entropy, strategies, bcm, 0, 0, 3);

    Assert.Multiple(() => {
      Assert.That(result.Length, Is.EqualTo(3),
        "Should still allocate one outer slot per channel.");
      foreach (var channel in result)
        Assert.That(channel.Length, Is.EqualTo(0),
          "Empty group → no blocks per channel.");
      Assert.That(reader.BitsRead, Is.EqualTo(0L),
        "Empty group must not consume any bits.");
    });
  }

  // ============================================================
  // Output-shape validation (the only thing we can verify without a
  // realistic entropy stream — the actual coefficient values are tested
  // through the round-trip integration suite once that is wired).
  // ============================================================

  /// <summary>For a small all-zero AC stream (every entropy.ReadInt(ctx) → 0)
  /// the decoder produces the right shape: numChannels × (W × H) blocks of
  /// 64 zero coefficients each.</summary>
  [Test]
  public void DecodeGroup_AllZerosStream_ProducesCorrectShape() {
    // CreateSimple with maxSymbol=0 → every ReadInt returns 0 (fixed-symbol
    // prefix code). With nzeros=0 for every block, the inner coefficient
    // loop is skipped entirely — so we exercise the per-block bookkeeping
    // path without depending on the exact coefficient-encoding contract.
    const int blocksWide = 4;
    const int blocksHigh = 3;
    const int numChannels = 3;
    var bytes = new byte[256];
    var bcm = JxlBlockContextMap.CreateDefault();
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(blocksWide, blocksHigh);
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, numContexts: 1024, maxSymbol: 0);

    var result = JxlAcDecoder.DecodeGroup(
      reader, entropy, strategies, bcm,
      blocksWide, blocksHigh, numChannels);

    Assert.Multiple(() => {
      Assert.That(result.Length, Is.EqualTo(numChannels),
        "One outer slot per channel.");
      foreach (var channel in result) {
        Assert.That(channel.Length, Is.EqualTo(blocksWide * blocksHigh),
          "blocksWide × blocksHigh blocks per channel.");
        foreach (var block in channel) {
          Assert.That(block.Width, Is.EqualTo(8), "DCT8 width.");
          Assert.That(block.Height, Is.EqualTo(8), "DCT8 height.");
          Assert.That(block.Coefficients.Length, Is.EqualTo(64),
            "8 × 8 = 64 scan-order coefficients.");
          for (var k = 0; k < 64; ++k)
            Assert.That(block.Coefficients[k], Is.EqualTo(0),
              $"All-zero entropy stream → all-zero coefficients at index {k}.");
        }
      }
    });
  }

  /// <summary>Single-channel decode (e.g. a grayscale-only frame) works
  /// without falling into the libjxl {1,0,2} channel-order path.</summary>
  [Test]
  public void DecodeGroup_SingleChannel_AllZerosStream_ProducesCorrectShape() {
    var bytes = new byte[256];
    var bcm = JxlBlockContextMap.CreateDefault();
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(2, 2);
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, numContexts: 1024, maxSymbol: 0);

    var result = JxlAcDecoder.DecodeGroup(reader, entropy, strategies, bcm, 2, 2, numChannels: 1);

    Assert.Multiple(() => {
      Assert.That(result.Length, Is.EqualTo(1));
      Assert.That(result[0].Length, Is.EqualTo(4));
      foreach (var block in result[0])
        Assert.That(block.Coefficients.Length, Is.EqualTo(64));
    });
  }

  // ============================================================
  // Multi-block / large-strategy rejection
  // ============================================================

  /// <summary>
  /// A transform covering more than one block is read once, at the block it
  /// starts from, and holds a coefficient for every block it covers.
  /// </summary>
  [Test]
  public void DecodeGroup_Dct16Strategy_ReadsOneBlockOfFourBlocksWorth() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(2, 2);
    for (var y = 0; y < 2; ++y)
    for (var x = 0; x < 2; ++x)
      strategies[y][x] = JxlAcStrategyType.Dct16x16;

    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    var blocks = JxlAcDecoder.DecodeGroup(reader, entropy, strategies, bcm, 2, 2, 3);
    Assert.Multiple(() => {
      // Sixteen pixels across and down: four blocks' worth of coefficients.
      Assert.That(blocks[0][0].Coefficients, Has.Length.EqualTo(4 * 64));
      Assert.That(blocks[0][0].Width, Is.EqualTo(16));
      Assert.That(blocks[0][0].Height, Is.EqualTo(16));
    });
  }

  /// <summary>
  /// A cell marked as covered by a neighbour names no transform of its own, so
  /// nothing is read for it.
  /// </summary>
  [Test]
  public void DecodeGroup_CoveredByNeighbour_ReadsNothingForThatCell() {
    var bytes = new byte[16];
    var bcm = JxlBlockContextMap.CreateDefault();
    var strategies = JxlAcStrategyDecoder.CreateAllDct8x8(2, 2);
    strategies[0][1] = JxlAcStrategyDecoder.CoveredByNeighbour;
    var reader = new JxlBitReader(bytes, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    var blocks = JxlAcDecoder.DecodeGroup(reader, entropy, strategies, bcm, 2, 2, 3);
    Assert.That(blocks[0][1].Coefficients, Is.All.Zero);
  }

  // ============================================================
  // Helper sanity (libjxl context formulas)
  // ============================================================

  /// <summary>libjxl <c>NonZeroContext</c> spot checks against the formula
  /// (lib/jxl/ac_context.h):
  /// <code>non_zeros &lt; 8 ? non_zeros : 4 + non_zeros / 2;
  ///        return ctx * num_ctxs + block_ctx;</code>
  /// </summary>
  [Test]
  public void NonZeroContext_Formula_MatchesLibJxl() {
    Assert.Multiple(() => {
      // Predicted < 8: ctx = predicted; final = ctx * num_ctxs + block_ctx.
      Assert.That(JxlAcDecoder._NonZeroContext(3, 5, 15),
        Is.EqualTo(3 * 15 + 5));
      // Predicted >= 8: ctx = 4 + predicted/2.
      Assert.That(JxlAcDecoder._NonZeroContext(20, 7, 15),
        Is.EqualTo((4 + 20 / 2) * 15 + 7));
      // Saturation at 64.
      Assert.That(JxlAcDecoder._NonZeroContext(200, 0, 15),
        Is.EqualTo((4 + 64 / 2) * 15 + 0));
    });
  }

  /// <summary>libjxl <c>ZeroDensityContextsOffset</c>:
  /// <c>num_ctxs * kNonZeroBuckets + kZeroDensityContextCount * block_ctx</c>.
  /// </summary>
  [Test]
  public void ZeroDensityContextsOffset_Formula_MatchesLibJxl() {
    Assert.Multiple(() => {
      // For default map (num_ctxs = 15) and block_ctx = 0:
      //   15 * 37 + 458 * 0 = 555
      Assert.That(JxlAcDecoder._ZeroDensityContextsOffset(0, 15),
        Is.EqualTo(15 * 37 + 458 * 0));
      // block_ctx = 7:
      Assert.That(JxlAcDecoder._ZeroDensityContextsOffset(7, 15),
        Is.EqualTo(15 * 37 + 458 * 7));
    });
  }

  /// <summary>Constants exposed for downstream code match libjxl values.</summary>
  [Test]
  public void Constants_MatchLibJxl() {
    Assert.Multiple(() => {
      Assert.That(JxlAcDecoder.NonZeroBuckets, Is.EqualTo(37),
        "kNonZeroBuckets from ac_context.h.");
      Assert.That(JxlAcDecoder.ZeroDensityContextCount, Is.EqualTo(458),
        "kZeroDensityContextCount from ac_context.h.");
      Assert.That(JxlAcDecoder.CoeffFreqContext.Length, Is.EqualTo(64),
        "kCoeffFreqContext is a 64-entry table.");
      Assert.That(JxlAcDecoder.CoeffNumNonzeroContext.Length, Is.EqualTo(64),
        "kCoeffNumNonzeroContext is a 64-entry table.");
      // Spot-check: the last entry of CoeffNumNonzeroContext is 206 in libjxl.
      Assert.That(JxlAcDecoder.CoeffNumNonzeroContext[63], Is.EqualTo(206));
      // Spot-check: kCoeffFreqContext[63] = 30.
      Assert.That(JxlAcDecoder.CoeffFreqContext[63], Is.EqualTo(30));
    });
  }
}
