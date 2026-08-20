using System;
using System.Collections.Generic;
using System.Linq;

namespace FileFormat.Codecs.Mpeg1.Tests;

/// <summary>
/// The variable-length code tables of ISO/IEC 11172-2 Annex B, checked as codes rather than through
/// anything they decode.
/// </summary>
/// <remarks>
/// These tables hold six hundred-odd codes transcribed by eye from a printed annex, and one wrong bit
/// is invisible until some picture somewhere is subtly wrong. What can be checked without a stream is
/// the shape: how many codes each table has, that no code is a prefix of another, and that the values
/// they carry cover exactly the range the standard defines with no gap and no repeat. Between them
/// those catch a dropped line, a duplicated one, and a value typed twice.
/// <para/>
/// What they cannot catch is a code that is unique, in range, and attached to the wrong value. That
/// is what comparing whole decoded pictures against a reference decoder is for, and it is how these
/// tables were actually verified.
/// </remarks>
[TestFixture]
public sealed class Mpeg1VlcTableTests {

  /// <summary>Each table, by the number the standard prints it under, with how many codes it holds.</summary>
  /// <remarks>
  /// Named rather than passed, because the tables are internal to the codec while a test method NUnit
  /// can call has to be public. The lookup below is the whole of the indirection.
  /// </remarks>
  private static IEnumerable<TestCaseData> _Tables() {
    yield return new TestCaseData("B.1", 35).SetName("Table B.1 macroblock_address_increment");
    yield return new TestCaseData("B.2", 2).SetName("Table B.2 macroblock_type, I pictures");
    yield return new TestCaseData("B.3", 7).SetName("Table B.3 macroblock_type, P pictures");
    yield return new TestCaseData("B.4", 11).SetName("Table B.4 macroblock_type, B pictures");
    yield return new TestCaseData("B.9", 63).SetName("Table B.9 coded_block_pattern");
    yield return new TestCaseData("B.10", 33).SetName("Table B.10 motion_code");
    yield return new TestCaseData("B.12", 9).SetName("Table B.12 dct_dc_size_luminance");
    yield return new TestCaseData("B.13", 9).SetName("Table B.13 dct_dc_size_chrominance");
    yield return new TestCaseData("B.14", 113).SetName("Table B.14 dct_coeff_next");
  }

  private static Mpeg1VlcTable _Table(string number) => number switch {
    "B.1" => Mpeg1VlcTables.MacroblockAddressIncrement,
    "B.2" => Mpeg1VlcTables.IntraMacroblockType,
    "B.3" => Mpeg1VlcTables.PredictedMacroblockType,
    "B.4" => Mpeg1VlcTables.BidirectionalMacroblockType,
    "B.9" => Mpeg1VlcTables.CodedBlockPattern,
    "B.10" => Mpeg1VlcTables.MotionCode,
    "B.12" => Mpeg1VlcTables.LuminanceDcSize,
    "B.13" => Mpeg1VlcTables.ChrominanceDcSize,
    _ => Mpeg1VlcTables.Coefficient,
  };

  [TestCaseSource(nameof(_Tables))]
  [Category("Unit")]
  public void TheTableHoldsAsManyCodesAsTheStandardPrints(string number, int expected)
    => Assert.That(_Table(number).Entries.Count, Is.EqualTo(expected));

  [TestCaseSource(nameof(_Tables))]
  [Category("Unit")]
  public void NoCodeIsAPrefixOfAnother(string number, int _) {
    var table = _Table(number);
    var codes = table.Entries.Select(entry => entry.Code.Replace(" ", string.Empty)).ToArray();

    for (var i = 0; i < codes.Length; ++i)
      for (var j = 0; j < codes.Length; ++j)
        if (i != j && codes[j].StartsWith(codes[i], StringComparison.Ordinal))
          Assert.Fail($"{table.Name}: '{codes[i]}' is a prefix of '{codes[j]}'.");
  }

  [TestCaseSource(nameof(_Tables))]
  [Category("Unit")]
  public void TheTableDoesNotClaimMoreCodeSpaceThanExists(string number, int _) {
    // Every code of length L claims 2^(max - L) of the longest length's space. A prefix code can
    // leave space unclaimed — these tables all do, since the patterns nearest a start code are
    // deliberately undefined — but it can never claim more than there is.
    var table = _Table(number);
    var claimed = table.Entries.Sum(entry => 1L << (table.MaxLength - entry.Code.Replace(" ", string.Empty).Length));

    Assert.That(claimed, Is.LessThanOrEqualTo(1L << table.MaxLength));
  }

  [Test]
  [Category("Unit")]
  public void TableOneCarriesEveryIncrementFromOneToThirtyThreeAndTheTwoSpecials() {
    var values = Mpeg1VlcTables.MacroblockAddressIncrement.Entries.Select(entry => entry.Value).ToArray();

    Assert.That(values.Where(v => v > 0).OrderBy(v => v).ToArray(), Is.EqualTo(Enumerable.Range(1, 33).ToArray()));
    Assert.That(values, Does.Contain(Mpeg1VlcTables.Stuffing));
    Assert.That(values, Does.Contain(Mpeg1VlcTables.Escape));
  }

  [Test]
  [Category("Unit")]
  public void TableNineCarriesEveryPatternFromOneToSixtyThree()
    => Assert.That(Mpeg1VlcTables.CodedBlockPattern.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(1, 63).ToArray()));

  [Test]
  [Category("Unit")]
  public void TableTenCarriesEveryMotionCodeFromMinusSixteenToSixteen()
    => Assert.That(Mpeg1VlcTables.MotionCode.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(-16, 33).ToArray()));

  [Test]
  [Category("Unit")]
  public void TablesTwelveAndThirteenCarryEverySizeFromZeroToEight() {
    Assert.That(Mpeg1VlcTables.LuminanceDcSize.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 9).ToArray()));
    Assert.That(Mpeg1VlcTables.ChrominanceDcSize.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 9).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void TableFourteenCarriesEveryRunWithItsLevelsRunningFromOne() {
    // The standard prints Table B.14 grouped by run, each run's levels running from one upwards with
    // no gaps, so a dropped line shows as a gap and a duplicated one as a repeat.
    var pairs = _RunsAndLevels();

    Assert.That(pairs.Distinct().Count(), Is.EqualTo(pairs.Length), "a run and level pair appears twice");
    Assert.That(pairs.Select(pair => pair.Run).Distinct().OrderBy(run => run).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 32).ToArray()));

    foreach (var group in pairs.GroupBy(pair => pair.Run).OrderBy(group => group.Key)) {
      var levels = group.Select(pair => pair.Level).OrderBy(level => level).ToArray();
      Assert.That(levels, Is.EqualTo(Enumerable.Range(1, levels.Length).ToArray()), $"run {group.Key}");
    }
  }

  [Test]
  [Category("Unit")]
  public void TableFourteenReachesTheLevelsTheStandardStopsAt() {
    var pairs = _RunsAndLevels();

    Assert.That(pairs.Where(pair => pair.Run == 0).Max(pair => pair.Level), Is.EqualTo(40));
    Assert.That(pairs.Where(pair => pair.Run == 1).Max(pair => pair.Level), Is.EqualTo(18));
    Assert.That(pairs.Where(pair => pair.Run == 2).Max(pair => pair.Level), Is.EqualTo(5));
    Assert.That(pairs.Where(pair => pair.Run >= 17).Max(pair => pair.Level), Is.EqualTo(1));
    Assert.That(pairs.Length, Is.EqualTo(111), "the run-level codes, without End of Block and the escape");
  }

  [Test]
  [Category("Unit")]
  public void TheDefaultIntraMatrixIsTheOneTheStandardPrints() {
    // The four corners in raster order. This is also what catches a matrix left in the zig-zag order
    // the standard prints it in: scan position 63 is raster position 63 only by coincidence, but
    // scan position 7 is raster position 3.
    Assert.That(Mpeg1Quantisation.DefaultIntraMatrix[0], Is.EqualTo(8));
    Assert.That(Mpeg1Quantisation.DefaultIntraMatrix[7], Is.EqualTo(34));
    Assert.That(Mpeg1Quantisation.DefaultIntraMatrix[56], Is.EqualTo(27));
    Assert.That(Mpeg1Quantisation.DefaultIntraMatrix[63], Is.EqualTo(83));
    Assert.That(Mpeg1Quantisation.DefaultNonIntraMatrix.Distinct().ToArray(), Is.EqualTo(new byte[] { 16 }));
  }

  [Test]
  [Category("Unit")]
  public void TheZigZagScanVisitsEveryPositionOnce()
    => Assert.That(Mpeg1Quantisation.ZigZag.OrderBy(position => position).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 64).ToArray()));

  [Test]
  [Category("Unit")]
  public void DequantisationForcesEveryCoefficientOdd() {
    // The oddification of 11172-2 2.4.4.1, which keeps two conforming inverse transforms' rounding
    // from accumulating. Zero stays zero: an uncoded coefficient is not moved to one.
    //
    // The quantiser scale stops at 25 because beyond it these levels reconstruct past the range a
    // coefficient is defined over, and the saturation that follows the oddification can land on an
    // even value — which is the standard's own behaviour and is checked separately below.
    for (var level = -40; level <= 40; ++level)
      for (var scale = 1; scale <= 25; ++scale) {
        var intra = Mpeg1Quantisation.DequantiseIntra(level, scale, 16);
        var nonIntra = Mpeg1Quantisation.DequantiseNonIntra(level, scale, 16);

        Assert.That(intra == 0 || (intra & 1) != 0, Is.True, $"intra level {level} scale {scale} gave {intra}");
        Assert.That(nonIntra == 0 || (nonIntra & 1) != 0, Is.True, $"non-intra level {level} scale {scale} gave {nonIntra}");
      }

    Assert.That(Mpeg1Quantisation.DequantiseNonIntra(0, 8, 16), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DequantisationSaturatesRatherThanWrapping() {
    // 11172-2 2.4.4.1 saturates after the oddification and not before, so the negative limit is the
    // even -2048 rather than -2047.
    Assert.That(Mpeg1Quantisation.DequantiseIntra(255, 31, 83), Is.EqualTo(2047));
    Assert.That(Mpeg1Quantisation.DequantiseIntra(-255, 31, 83), Is.EqualTo(-2048));
    Assert.That(Mpeg1Quantisation.DequantiseNonIntra(255, 31, 255), Is.EqualTo(2047));
  }

  private static (int Run, int Level)[] _RunsAndLevels()
    => Mpeg1VlcTables.Coefficient.Entries
      .Where(entry => entry.Value >= 0)
      .Select(entry => (Run: Mpeg1VlcTables.RunOf(entry.Value), Level: Mpeg1VlcTables.LevelOf(entry.Value)))
      .ToArray();
}
