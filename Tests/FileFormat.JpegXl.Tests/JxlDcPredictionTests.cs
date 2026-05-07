using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Tests for <see cref="JxlDcPrediction"/> — Y-channel-driven DC prediction
/// for JPEG XL VarDCT chroma channels and DC-bucket selection for entropy
/// context lookup.
///
/// <para>libjxl reference (BSD-3-Clause):</para>
/// <list type="bullet">
///   <item><c>lib/jxl/dec_modular.cc DequantDC</c> — applies DCFactors to Y
///         and adds to X / B</item>
///   <item><c>lib/jxl/chroma_from_luma.h ColorCorrelation::DCFactors</c> —
///         per-tile factors</item>
///   <item><c>lib/jxl/ac_context.h BlockCtxMap::DCContext</c> — DC bucket
///         from threshold compare</item>
/// </list>
/// </summary>
[TestFixture]
internal sealed class JxlDcPredictionTests {

  // ---------------------------------------------------------------------
  // PredictXandBFromY — the trivial test required by the task spec.
  // ---------------------------------------------------------------------

  /// <summary>The trivial test required by the task spec: DC prediction
  /// with all-zero Y means X/B unchanged.</summary>
  [Test]
  public void PredictXandBFromY_AllZeroY_LeavesXAndBUnchanged() {
    var cmap = JxlColorCorrelationMap.CreateZero(64, 64);
    cmap.CmapX[0] = 50;            // arbitrary nonzero
    cmap.CmapY[0] = -30;
    var y = new short[64];          // all zero
    var x = new short[64];
    var b = new short[64];
    for (var i = 0; i < 64; ++i) { x[i] = (short)(100 + i); b[i] = (short)(200 + i); }
    var xExpected = (short[])x.Clone();
    var bExpected = (short[])b.Clone();

    JxlDcPrediction.PredictXandBFromY(
      yDcs: y, xDcsResidual: x, bDcsResidual: b,
      cmap: cmap, groupBlocksWide: 8, groupBlocksHigh: 8,
      groupX: 0, groupY: 0);

    Assert.Multiple(() => {
      for (var i = 0; i < 64; ++i) {
        Assert.That(x[i], Is.EqualTo(xExpected[i]), $"X[{i}]");
        Assert.That(b[i], Is.EqualTo(bExpected[i]), $"B[{i}]");
      }
    });
  }

  /// <summary>Zero cmap factors are also a guaranteed no-op even for nonzero
  /// Y values (libjxl: ZeroFillImage'd default cmap).</summary>
  [Test]
  public void PredictXandBFromY_AllZeroCmap_LeavesXAndBUnchanged() {
    var cmap = JxlColorCorrelationMap.CreateZero(64, 64);   // all zeros
    var y = new short[64];
    Array.Fill(y, (short)123);
    var x = new short[64];
    Array.Fill(x, (short)10);
    var b = new short[64];
    Array.Fill(b, (short)-20);

    JxlDcPrediction.PredictXandBFromY(
      yDcs: y, xDcsResidual: x, bDcsResidual: b,
      cmap: cmap, groupBlocksWide: 8, groupBlocksHigh: 8,
      groupX: 0, groupY: 0);

    Assert.Multiple(() => {
      for (var i = 0; i < 64; ++i) {
        Assert.That(x[i], Is.EqualTo((short)10), $"X[{i}] should be unchanged.");
        Assert.That(b[i], Is.EqualTo((short)-20), $"B[{i}] should be unchanged.");
      }
    });
  }

  /// <summary>With cmap factor 127 and Y = 100, prediction adds (127*100)/128
  /// = 12700 >> 7 = 99 to X residual.</summary>
  [Test]
  public void PredictXandBFromY_PositiveFactorWithYActive_AddsScaledY() {
    var cmap = JxlColorCorrelationMap.CreateZero(64, 64);
    cmap.CmapX[0] = 127;
    cmap.CmapY[0] = 64;            // (64*100)>>7 = 6400>>7 = 50
    var y = new short[64]; Array.Fill(y, (short)100);
    var x = new short[64]; Array.Fill(x, (short)5);
    var b = new short[64]; Array.Fill(b, (short)-7);

    JxlDcPrediction.PredictXandBFromY(
      yDcs: y, xDcsResidual: x, bDcsResidual: b,
      cmap: cmap, groupBlocksWide: 8, groupBlocksHigh: 8,
      groupX: 0, groupY: 0);

    Assert.Multiple(() => {
      Assert.That(x[0], Is.EqualTo((short)(5 + 99)));    // 5 + (127*100)>>7
      Assert.That(b[0], Is.EqualTo((short)(-7 + 50)));   // -7 + (64*100)>>7
    });
  }

  /// <summary>Negative factor: arithmetic-shift-right keeps sign — (-64 * 100) >> 7
  /// = -6400 >> 7 = -50.</summary>
  [Test]
  public void PredictXandBFromY_NegativeFactor_SubtractsScaledY() {
    var cmap = JxlColorCorrelationMap.CreateZero(64, 64);
    cmap.CmapX[0] = -64;
    var y = new short[64]; Array.Fill(y, (short)100);
    var x = new short[64];                                  // residual = 0

    JxlDcPrediction.PredictXandBFromY(
      yDcs: y, xDcsResidual: x, bDcsResidual: new short[64],
      cmap: cmap, groupBlocksWide: 8, groupBlocksHigh: 8,
      groupX: 0, groupY: 0);

    Assert.That(x[0], Is.EqualTo((short)-50));
  }

  /// <summary>Tile boundary at block 8: with a 2×1 tile cmap (128×64 image)
  /// and only the left tile's factor set, blocks in the right tile are
  /// untouched.</summary>
  [Test]
  public void PredictXandBFromY_FactorAppliesPerTile() {
    var cmap = JxlColorCorrelationMap.CreateZero(128, 64);
    cmap.CmapX[0] = 127;            // left tile only
    var y = new short[16 * 8]; Array.Fill(y, (short)100);
    var x = new short[16 * 8];
    var b = new short[16 * 8];

    JxlDcPrediction.PredictXandBFromY(
      yDcs: y, xDcsResidual: x, bDcsResidual: b,
      cmap: cmap, groupBlocksWide: 16, groupBlocksHigh: 8,
      groupX: 0, groupY: 0);

    Assert.Multiple(() => {
      Assert.That(x[0], Is.EqualTo((short)99), "Left-tile block 0 corrected.");
      Assert.That(x[7], Is.EqualTo((short)99), "Left-tile block 7 corrected.");
      Assert.That(x[8], Is.EqualTo((short)0), "Right-tile block 8 untouched.");
      Assert.That(x[15], Is.EqualTo((short)0), "Right-tile block 15 untouched.");
    });
  }

  /// <summary>Length validation rejects mismatched-length arrays
  /// (libjxl always pre-allocates by group size).</summary>
  [Test]
  public void PredictXandBFromY_LengthMismatch_Throws() {
    var cmap = JxlColorCorrelationMap.CreateZero(64, 64);
    Assert.Throws<ArgumentException>(() => JxlDcPrediction.PredictXandBFromY(
      yDcs: new short[64], xDcsResidual: new short[63], bDcsResidual: new short[64],
      cmap: cmap, groupBlocksWide: 8, groupBlocksHigh: 8,
      groupX: 0, groupY: 0));
  }

  /// <summary>groupX must be a multiple of 8 (block size). libjxl groups are
  /// pre-aligned to the block grid.</summary>
  [Test]
  public void PredictXandBFromY_NonBlockAlignedGroupOrigin_Throws() {
    var cmap = JxlColorCorrelationMap.CreateZero(128, 128);
    Assert.Throws<ArgumentException>(() => JxlDcPrediction.PredictXandBFromY(
      yDcs: new short[64], xDcsResidual: new short[64], bDcsResidual: new short[64],
      cmap: cmap, groupBlocksWide: 8, groupBlocksHigh: 8,
      groupX: 7, groupY: 0));
  }

  // ---------------------------------------------------------------------
  // DcBucketIndex — libjxl BlockCtxMap::DCContext.
  // ---------------------------------------------------------------------

  /// <summary>Empty thresholds → always bucket 0 (matches libjxl's default
  /// block context map with <c>num_dc_ctxs = 1</c>).</summary>
  [Test]
  public void DcBucketIndex_EmptyThresholds_AlwaysReturnsZero() {
    var thresholds = Array.Empty<int>();
    Assert.Multiple(() => {
      Assert.That(JxlDcPrediction.DcBucketIndex(0, thresholds), Is.EqualTo(0));
      Assert.That(JxlDcPrediction.DcBucketIndex(1000, thresholds), Is.EqualTo(0));
      Assert.That(JxlDcPrediction.DcBucketIndex(-1000, thresholds), Is.EqualTo(0));
    });
  }

  /// <summary>One threshold = 50: dc &gt; 50 → bucket 1, else bucket 0.</summary>
  [Test]
  public void DcBucketIndex_SingleThreshold_PartitionsAtBoundary() {
    var thresholds = new[] { 50 };
    Assert.Multiple(() => {
      Assert.That(JxlDcPrediction.DcBucketIndex(49, thresholds), Is.EqualTo(0));
      Assert.That(JxlDcPrediction.DcBucketIndex(50, thresholds), Is.EqualTo(0)); // strict >
      Assert.That(JxlDcPrediction.DcBucketIndex(51, thresholds), Is.EqualTo(1));
      Assert.That(JxlDcPrediction.DcBucketIndex(-100, thresholds), Is.EqualTo(0));
    });
  }

  /// <summary>Multiple sorted thresholds produce 0..N buckets (libjxl bound:
  /// thresholds.size ≤ 15 from 4-bit count field).</summary>
  [Test]
  public void DcBucketIndex_MultipleThresholds_CountsExceedances() {
    var thresholds = new[] { -10, 0, 10, 100 };
    Assert.Multiple(() => {
      Assert.That(JxlDcPrediction.DcBucketIndex(-50, thresholds), Is.EqualTo(0));
      Assert.That(JxlDcPrediction.DcBucketIndex(-5, thresholds), Is.EqualTo(1));
      Assert.That(JxlDcPrediction.DcBucketIndex(5, thresholds), Is.EqualTo(2));
      Assert.That(JxlDcPrediction.DcBucketIndex(50, thresholds), Is.EqualTo(3));
      Assert.That(JxlDcPrediction.DcBucketIndex(500, thresholds), Is.EqualTo(4));
    });
  }

  [Test]
  public void DcBucketIndex_NullThresholds_Throws() {
    Assert.Throws<ArgumentNullException>(() => JxlDcPrediction.DcBucketIndex(0, null!));
  }
}
