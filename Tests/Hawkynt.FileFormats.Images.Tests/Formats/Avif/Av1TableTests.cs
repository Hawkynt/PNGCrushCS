using System;
using System.Linq;
using FileFormat.Avif.Codec;

namespace FileFormat.Avif.Tests;

/// <summary>
/// The constant tables the AV1 decoder is built on. These are copied from the reference encoder
/// rather than derived, so what is worth testing is that the copy is intact and correctly shaped —
/// a table that has drifted by one entry produces a picture that is plausible and wrong.
/// </summary>
[TestFixture]
public sealed class Av1TableTests {

  [Test]
  [Category("Unit")]
  public void DefaultCdfs_MatchTheSpecificationsPartitionTables() {
    // AV1 section 9.4 Default_Partition_Cdf, for the 8x8, 16x16 and 128x128 groups.
    Assert.Multiple(() => {
      Assert.That(_Cdf(Av1DefaultCdfTables.Partition, Av1DefaultCdfTables.PartitionStride, 0, 5),
        Is.EqualTo(new ushort[] { 19132, 25510, 30392, 32768, 0 }));
      Assert.That(_Cdf(Av1DefaultCdfTables.Partition, Av1DefaultCdfTables.PartitionStride, 4, 11),
        Is.EqualTo(new ushort[] { 15597, 20929, 24571, 26706, 27664, 28821, 29601, 30571, 31902, 32768, 0 }));
      Assert.That(_Cdf(Av1DefaultCdfTables.Partition, Av1DefaultCdfTables.PartitionStride, 16, 9),
        Is.EqualTo(new ushort[] { 27899, 28219, 28529, 32484, 32539, 32619, 32639, 32768, 0 }));
    });
  }

  /// <summary>Every CDF row has to be non-decreasing and terminate at 32768, or the arithmetic
  /// decoder walks off it.</summary>
  [Test]
  [Category("Unit")]
  public void EveryDefaultCdfRow_IsMonotonicAndTerminated(
    [Values("Partition", "KeyFrameYMode", "UvMode", "AngleDelta", "IntraExtTx", "CflAlpha", "Skip",
            "FilterIntra", "TxSize", "SegmentId", "PaletteYMode")] string name
  ) {
    var (table, stride) = name switch {
      "Partition" => (Av1DefaultCdfTables.Partition, Av1DefaultCdfTables.PartitionStride),
      "KeyFrameYMode" => (Av1DefaultCdfTables.KeyFrameYMode, Av1DefaultCdfTables.KeyFrameYModeStride),
      "UvMode" => (Av1DefaultCdfTables.UvMode, Av1DefaultCdfTables.UvModeStride),
      "AngleDelta" => (Av1DefaultCdfTables.AngleDelta, Av1DefaultCdfTables.AngleDeltaStride),
      "IntraExtTx" => (Av1DefaultCdfTables.IntraExtTx, Av1DefaultCdfTables.IntraExtTxStride),
      "CflAlpha" => (Av1DefaultCdfTables.CflAlpha, Av1DefaultCdfTables.CflAlphaStride),
      "Skip" => (Av1DefaultCdfTables.Skip, Av1DefaultCdfTables.SkipStride),
      "FilterIntra" => (Av1DefaultCdfTables.FilterIntra, Av1DefaultCdfTables.FilterIntraStride),
      "TxSize" => (Av1DefaultCdfTables.TxSize, Av1DefaultCdfTables.TxSizeStride),
      "SegmentId" => (Av1DefaultCdfTables.SegmentId, Av1DefaultCdfTables.SegmentIdStride),
      _ => (Av1DefaultCdfTables.PaletteYMode, Av1DefaultCdfTables.PaletteYModeStride),
    };

    Assert.That(table.Length % stride, Is.Zero, "the table must be a whole number of rows");
    for (var row = 0; row * stride < table.Length; ++row)
      _AssertRowIsAValidCdf(table.AsSpan(row * stride, stride), $"{name} row {row}");
  }

  [Test]
  [Category("Unit")]
  public void EveryCoefficientCdfRow_IsMonotonicAndTerminated(
    [Values("TxbSkip", "EobExtra", "DcSign", "CoeffBase", "CoeffBaseEob", "CoeffBr", "EobPt16", "EobPt1024")] string name
  ) {
    var (table, stride) = name switch {
      "TxbSkip" => (Av1CoefficientCdfTables.TxbSkip, Av1CoefficientCdfTables.TxbSkipStride),
      "EobExtra" => (Av1CoefficientCdfTables.EobExtra, Av1CoefficientCdfTables.EobExtraStride),
      "DcSign" => (Av1CoefficientCdfTables.DcSign, Av1CoefficientCdfTables.DcSignStride),
      "CoeffBase" => (Av1CoefficientCdfTables.CoeffBase, Av1CoefficientCdfTables.CoeffBaseStride),
      "CoeffBaseEob" => (Av1CoefficientCdfTables.CoeffBaseEob, Av1CoefficientCdfTables.CoeffBaseEobStride),
      "CoeffBr" => (Av1CoefficientCdfTables.CoeffBr, Av1CoefficientCdfTables.CoeffBrStride),
      "EobPt16" => (Av1CoefficientCdfTables.EobPt16, Av1CoefficientCdfTables.EobPt16Stride),
      _ => (Av1CoefficientCdfTables.EobPt1024, Av1CoefficientCdfTables.EobPt1024Stride),
    };

    Assert.That(table.Length % stride, Is.Zero);
    for (var row = 0; row * stride < table.Length; ++row)
      _AssertRowIsAValidCdf(table.AsSpan(row * stride, stride), $"{name} row {row}");
  }

  /// <summary>A scan order must visit every coefficient of its transform exactly once.</summary>
  [Test]
  [Category("Unit")]
  public void EveryScanOrder_IsAPermutationOfItsTransformPositions() {
    for (var txSize = 0; txSize < Av1Constants.TxSizesAll; ++txSize)
      for (var txType = 0; txType < Av1Constants.TxTypes; ++txType) {
        var scan = Av1ScanTables.GetScan(txSize, txType);
        var expected = Math.Min(32, Av1StructureTables.TxWidth[txSize]) * Math.Min(32, Av1StructureTables.TxHeight[txSize]);

        Assert.That(scan.Length, Is.EqualTo(expected), $"tx {txSize} type {txType}");
        Assert.That(scan.ToArray().Distinct().Count(), Is.EqualTo(expected), $"tx {txSize} type {txType} repeats a position");
        Assert.That(scan.ToArray().Max(), Is.EqualTo(expected - 1), $"tx {txSize} type {txType} leaves a gap");
      }
  }

  /// <summary>The quantiser lookups are the AV1 specification's own, and a lossless frame relies on
  /// the first entry being exactly four.</summary>
  [Test]
  [Category("Unit")]
  public void QuantizerLookups_HaveTheSpecificationsEndpoints() {
    Assert.Multiple(() => {
      Assert.That(Av1QuantizerTables.DcQLookup8[0], Is.EqualTo(4));
      Assert.That(Av1QuantizerTables.AcQLookup8[0], Is.EqualTo(4));
      Assert.That(Av1QuantizerTables.DcQLookup8[255], Is.EqualTo(1336));
      Assert.That(Av1QuantizerTables.AcQLookup8[255], Is.EqualTo(1828));
      Assert.That(Av1QuantizerTables.DcQLookup8.Length, Is.EqualTo(256));
      Assert.That(Av1QuantizerTables.AcQLookup12.Length, Is.EqualTo(256));
    });
  }

  /// <summary>
  /// The quantiser matrices are indexed by an offset table that libaom builds by walking the
  /// transform sizes; if the offsets and the matrix lengths disagree, coefficients get weights from
  /// the wrong transform.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void QuantizerMatrixOffsets_CoverEachLevelExactlyOnce() {
    var covered = 0;
    for (var txSize = 0; txSize < Av1Constants.TxSizesAll; ++txSize) {
      var adjusted = Av1Structure.AdjustedTxSize((Av1TxSize)txSize);
      if ((int)adjusted != txSize)
        continue;

      covered += Av1StructureTables.TxWidth[txSize] * Av1StructureTables.TxHeight[txSize];
    }

    Assert.Multiple(() => {
      Assert.That(covered, Is.EqualTo(Av1QuantizerMatrixTables.LevelStride));
      Assert.That(Av1QuantizerMatrixTables.InverseMatrices.Length,
        Is.EqualTo(Av1QuantizerMatrixTables.Levels * 2 * Av1QuantizerMatrixTables.LevelStride));
      Assert.That(Av1QuantizerMatrixTables.GetInverse(0, 0, Av1TxSize.Tx4x4).ToArray(),
        Is.EqualTo(new byte[] { 32, 43, 73, 97, 43, 67, 94, 110, 73, 94, 137, 150, 97, 110, 150, 200 }));
    });
  }

  /// <summary>The lossless transform must be exactly reversible, or "lossless" is a lie.</summary>
  [Test]
  [Category("Unit")]
  public void WalshHadamard_RoundTripsEveryResidualAnEightBitPictureCanProduce() {
    var random = new Random(97);
    var original = new int[16];
    var working = new int[16];

    for (var trial = 0; trial < 20000; ++trial) {
      for (var i = 0; i < 16; ++i)
        original[i] = random.Next(-255, 256);

      original.CopyTo(working, 0);
      Av1ForwardTransform.WalshHadamard4x4(working);

      // The encoder codes the raw coefficients and the decoder dequantises them by four, which the
      // inverse transform then shifts back out.
      for (var i = 0; i < 16; ++i)
        working[i] *= 4;

      Av1InverseTransform.InverseWalshHadamard4x4(working);
      Assert.That(working, Is.EqualTo(original), $"trial {trial}");
    }
  }

  private static ushort[] _Cdf(ushort[] table, int stride, int row, int length) =>
    table.AsSpan(row * stride, length).ToArray();

  private static void _AssertRowIsAValidCdf(ReadOnlySpan<ushort> row, string what) {
    var used = false;
    foreach (var value in row)
      used |= value != 0;

    // Some tables reserve rows for combinations the format never signals; libaom leaves those zero.
    if (!used)
      return;

    var terminator = -1;
    for (var i = 0; i < row.Length; ++i) {
      if (row[i] != 32768)
        continue;

      terminator = i;
      break;
    }

    Assert.That(terminator, Is.GreaterThan(0), $"{what} never reaches 32768");
    for (var i = 1; i <= terminator; ++i)
      Assert.That(row[i], Is.GreaterThanOrEqualTo(row[i - 1]), $"{what} decreases at {i}");
  }
}
