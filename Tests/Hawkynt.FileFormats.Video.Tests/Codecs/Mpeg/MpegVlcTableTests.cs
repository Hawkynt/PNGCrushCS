using System;
using System.Collections.Generic;
using System.Linq;

namespace FileFormat.Codecs.Mpeg.Tests;

/// <summary>
/// The variable-length code tables of ISO/IEC 11172-2 Annex B and ISO/IEC 13818-2 Annex B, checked as
/// codes rather than through anything they decode.
/// </summary>
/// <remarks>
/// These tables hold nine hundred-odd codes transcribed by eye from two printed annexes, and one
/// wrong bit is invisible until some picture somewhere is subtly wrong. What can be checked without a
/// stream is the shape: how many codes each table has, that no code is a prefix of another, and that
/// the values they carry cover exactly the range the standard defines with no gap and no repeat.
/// Between them those catch a dropped line, a duplicated one, and a value typed twice.
/// <para/>
/// Table B.15 gets one more check than the rest, and it is the strongest one here: it codes exactly
/// the same hundred and eleven run and level pairs as Table B.14, differently. Two independently
/// transcribed tables agreeing on their whole value set is a great deal harder to achieve by accident
/// than either being internally consistent.
/// <para/>
/// What none of them can catch is a code that is unique, in range, and attached to the wrong value.
/// That is what comparing whole decoded pictures against a reference decoder is for, and it is how
/// these tables were actually verified.
/// </remarks>
[TestFixture]
public sealed class MpegVlcTableTests {

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
    yield return new TestCaseData("B.9+0", 64).SetName("13818-2 Table B.9 coded_block_pattern with zero");
    yield return new TestCaseData("B.10", 33).SetName("Table B.10 motion_code");
    yield return new TestCaseData("B.12", 9).SetName("11172-2 Table B.12 dct_dc_size_luminance");
    yield return new TestCaseData("B.13", 9).SetName("11172-2 Table B.13 dct_dc_size_chrominance");
    yield return new TestCaseData("B.12/2", 12).SetName("13818-2 Table B.12 dct_dc_size_luminance");
    yield return new TestCaseData("B.13/2", 12).SetName("13818-2 Table B.13 dct_dc_size_chrominance");
    yield return new TestCaseData("B.14", 113).SetName("Table B.14 dct_coeff_next");
    yield return new TestCaseData("B.15", 113).SetName("13818-2 Table B.15 dct_coefficients_1");
  }

  private static MpegVlcTable _Table(string number) => number switch {
    "B.1" => MpegVlcTables.MacroblockAddressIncrement,
    "B.2" => MpegVlcTables.IntraMacroblockType,
    "B.3" => MpegVlcTables.PredictedMacroblockType,
    "B.4" => MpegVlcTables.BidirectionalMacroblockType,
    "B.9" => MpegVlcTables.CodedBlockPattern,
    "B.9+0" => MpegVlcTables.CodedBlockPatternWithZero,
    "B.10" => MpegVlcTables.MotionCode,
    "B.12" => MpegVlcTables.Mpeg1LuminanceDcSize,
    "B.13" => MpegVlcTables.Mpeg1ChrominanceDcSize,
    "B.12/2" => MpegVlcTables.Mpeg2LuminanceDcSize,
    "B.13/2" => MpegVlcTables.Mpeg2ChrominanceDcSize,
    "B.15" => MpegVlcTables.IntraCoefficient,
    _ => MpegVlcTables.Coefficient,
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
    var values = MpegVlcTables.MacroblockAddressIncrement.Entries.Select(entry => entry.Value).ToArray();

    Assert.That(values.Where(v => v > 0).OrderBy(v => v).ToArray(), Is.EqualTo(Enumerable.Range(1, 33).ToArray()));
    Assert.That(values, Does.Contain(MpegVlcTables.Stuffing));
    Assert.That(values, Does.Contain(MpegVlcTables.Escape));
  }

  [Test]
  [Category("Unit")]
  public void TableNineCarriesEveryPatternFromOneToSixtyThree()
    => Assert.That(MpegVlcTables.CodedBlockPattern.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(1, 63).ToArray()));

  [Test]
  [Category("Unit")]
  public void TheChrominanceCodedBlockPatternTableAddsOnlyTheZeroPattern() {
    // 13818-2 prints Table B.9 as MPEG-1's plus a row for a pattern of zero, which it notes may not
    // be used with 4:2:0. Everything else has to be the same table, or a 4:2:2 stream and a 4:2:0
    // stream would be reading the same field two different ways.
    var with = MpegVlcTables.CodedBlockPatternWithZero.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray();

    Assert.That(with, Is.EqualTo(Enumerable.Range(0, 64).ToArray()));
    Assert.That(
      MpegVlcTables.CodedBlockPatternWithZero.Entries.Where(entry => entry.Value != 0),
      Is.EqualTo(MpegVlcTables.CodedBlockPattern.Entries));
  }

  [Test]
  [Category("Unit")]
  public void TableTenCarriesEveryMotionCodeFromMinusSixteenToSixteen()
    => Assert.That(MpegVlcTables.MotionCode.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(-16, 33).ToArray()));

  [Test]
  [Category("Unit")]
  public void TablesTwelveAndThirteenCarryEverySizeFromZeroToEight() {
    Assert.That(MpegVlcTables.Mpeg1LuminanceDcSize.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 9).ToArray()));
    Assert.That(MpegVlcTables.Mpeg1ChrominanceDcSize.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 9).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void TheMpeg2DcSizeTablesReachEleven() {
    // Three sizes further than MPEG-1's, which is what intra_dc_precision needs: nine, ten and eleven
    // bits of DC differential. The two tables agree on every size MPEG-1 has and only then diverge.
    Assert.That(MpegVlcTables.Mpeg2LuminanceDcSize.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 12).ToArray()));
    Assert.That(MpegVlcTables.Mpeg2ChrominanceDcSize.Entries.Select(entry => entry.Value).OrderBy(v => v).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 12).ToArray()));

    foreach (var (code, value) in MpegVlcTables.Mpeg1LuminanceDcSize.Entries)
      Assert.That(MpegVlcTables.Mpeg2LuminanceDcSize.Entries, Does.Contain((code, value)));

    foreach (var (code, value) in MpegVlcTables.Mpeg1ChrominanceDcSize.Entries)
      Assert.That(MpegVlcTables.Mpeg2ChrominanceDcSize.Entries, Does.Contain((code, value)));
  }

  [Test]
  [Category("Unit")]
  public void TableFourteenCarriesEveryRunWithItsLevelsRunningFromOne() {
    // The standard prints Table B.14 grouped by run, each run's levels running from one upwards with
    // no gaps, so a dropped line shows as a gap and a duplicated one as a repeat.
    var pairs = _RunsAndLevels(MpegVlcTables.Coefficient);

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
    var pairs = _RunsAndLevels(MpegVlcTables.Coefficient);

    Assert.That(pairs.Where(pair => pair.Run == 0).Max(pair => pair.Level), Is.EqualTo(40));
    Assert.That(pairs.Where(pair => pair.Run == 1).Max(pair => pair.Level), Is.EqualTo(18));
    Assert.That(pairs.Where(pair => pair.Run == 2).Max(pair => pair.Level), Is.EqualTo(5));
    Assert.That(pairs.Where(pair => pair.Run >= 17).Max(pair => pair.Level), Is.EqualTo(1));
    Assert.That(pairs.Length, Is.EqualTo(111), "the run-level codes, without End of Block and the escape");
  }

  [Test]
  [Category("Unit")]
  public void TableFifteenCodesTheSameRunsAndLevelsAsTableFourteen() {
    // The check that is worth more than the rest of this file. B.14 and B.15 are two codings of one
    // set of run and level pairs, transcribed separately from two pages; if either has a dropped,
    // duplicated or mistyped value, the two sets stop being equal. The codes themselves differ
    // completely and are not compared.
    var fourteen = _RunsAndLevels(MpegVlcTables.Coefficient).OrderBy(p => p.Run).ThenBy(p => p.Level).ToArray();
    var fifteen = _RunsAndLevels(MpegVlcTables.IntraCoefficient).OrderBy(p => p.Run).ThenBy(p => p.Level).ToArray();

    Assert.That(fifteen, Is.EqualTo(fourteen));
  }

  [Test]
  [Category("Unit")]
  public void TableFifteenEndsABlockWithItsOwnCodeAndNotTableFourteens() {
    // B.14 says End of Block with '10' and B.15 with '0110', and in B.15 '10' is a run of nought and
    // a level of one. Reading an intra_vlc_format picture with B.14's End of Block would end every
    // block at its first coefficient.
    var endOfBlock = MpegVlcTables.IntraCoefficient.Entries.Single(entry => entry.Value == MpegVlcTables.EndOfBlock);
    var levelOne = MpegVlcTables.IntraCoefficient.Entries.Single(entry => entry.Value == 1);

    Assert.That(endOfBlock.Code.Replace(" ", string.Empty), Is.EqualTo("0110"));
    Assert.That(levelOne.Code.Replace(" ", string.Empty), Is.EqualTo("10"));
    Assert.That(
      MpegVlcTables.IntraCoefficient.Entries.Single(entry => entry.Value == MpegVlcTables.CoefficientEscape).Code
        .Replace(" ", string.Empty),
      Is.EqualTo("000001"));
  }

  [Test]
  [Category("Unit")]
  public void TheDefaultIntraMatrixIsTheOneTheStandardPrints() {
    // The four corners in raster order. This is also what catches a matrix left in the zig-zag order
    // the standard prints it in: scan position 63 is raster position 63 only by coincidence, but
    // scan position 7 is raster position 3.
    Assert.That(MpegQuantisation.DefaultIntraMatrix[0], Is.EqualTo(8));
    Assert.That(MpegQuantisation.DefaultIntraMatrix[7], Is.EqualTo(34));
    Assert.That(MpegQuantisation.DefaultIntraMatrix[56], Is.EqualTo(27));
    Assert.That(MpegQuantisation.DefaultIntraMatrix[63], Is.EqualTo(83));
    Assert.That(MpegQuantisation.DefaultNonIntraMatrix.Distinct().ToArray(), Is.EqualTo(new byte[] { 16 }));
  }

  [Test]
  [Category("Unit")]
  public void BothScansVisitEveryPositionOnceAndAreNotTheSame() {
    Assert.That(MpegQuantisation.ZigZagScan.OrderBy(position => position).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 64).ToArray()));
    Assert.That(MpegQuantisation.AlternateScan.OrderBy(position => position).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 64).ToArray()));
    Assert.That(MpegQuantisation.AlternateScan, Is.Not.EqualTo(MpegQuantisation.ZigZagScan));
  }

  [Test]
  [Category("Unit")]
  public void TheAlternateScanRunsDownTheBlockBeforeItRunsAcross() {
    // 13818-2 Figure 7-3, which starts by taking the whole first column: raster positions 0, 8, 16
    // and 24 are the first four it visits, where the zig-zag takes 0, 1, 8, 16. Both end at 63.
    Assert.That(MpegQuantisation.AlternateScan[..4], Is.EqualTo(new[] { 0, 8, 16, 24 }));
    Assert.That(MpegQuantisation.ZigZagScan[..4], Is.EqualTo(new[] { 0, 1, 8, 16 }));
    Assert.That(MpegQuantisation.AlternateScan[63], Is.EqualTo(63));
  }

  [Test]
  [Category("Unit")]
  public void DequantisationForcesEveryCoefficientOddInMpeg1() {
    // The oddification of 11172-2 2.4.4.1, which keeps two conforming inverse transforms' rounding
    // from accumulating. Zero stays zero: an uncoded coefficient is not moved to one.
    //
    // The quantiser scale stops at 25 because beyond it these levels reconstruct past the range a
    // coefficient is defined over, and the saturation that follows the oddification can land on an
    // even value — which is the standard's own behaviour and is checked separately below.
    for (var level = -40; level <= 40; ++level)
      for (var scale = 1; scale <= 25; ++scale) {
        var intra = MpegQuantisation.DequantiseIntraMpeg1(level, scale, 16);
        var nonIntra = MpegQuantisation.DequantiseNonIntraMpeg1(level, scale, 16);

        Assert.That(intra == 0 || (intra & 1) != 0, Is.True, $"intra level {level} scale {scale} gave {intra}");
        Assert.That(nonIntra == 0 || (nonIntra & 1) != 0, Is.True, $"non-intra level {level} scale {scale} gave {nonIntra}");
      }

    Assert.That(MpegQuantisation.DequantiseNonIntraMpeg1(0, 8, 16), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DequantisationSaturatesRatherThanWrapping() {
    // 11172-2 2.4.4.1 saturates after the oddification and not before, so the negative limit is the
    // even -2048 rather than -2047.
    Assert.That(MpegQuantisation.DequantiseIntraMpeg1(255, 31, 83), Is.EqualTo(2047));
    Assert.That(MpegQuantisation.DequantiseIntraMpeg1(-255, 31, 83), Is.EqualTo(-2048));
    Assert.That(MpegQuantisation.DequantiseNonIntraMpeg1(255, 31, 255), Is.EqualTo(2047));
  }

  [Test]
  [Category("Unit")]
  public void TheTwoStandardsAgreeOnWhatALevelReconstructsTo() {
    // MPEG-2 divides by thirty-two where MPEG-1 divides by sixteen, and an MPEG-2 quantiser_scale is
    // twice the code where MPEG-1's is the code — so the two arrive at the same number, give or take
    // the oddification MPEG-2 does not do. Checking that here is what makes the two divisors
    // readable as the same arithmetic rather than as a difference nobody accounted for.
    for (var level = 1; level <= 40; ++level)
      for (var code = 1; code <= 31; ++code) {
        var mpeg1 = MpegQuantisation.DequantiseIntraMpeg1(level, code, 16);
        var mpeg2 = MpegQuantisation.DequantiseIntraMpeg2(level, MpegQuantisation.ScaleOf(code, nonLinear: false), 16);

        Assert.That(Math.Abs(mpeg1 - mpeg2), Is.LessThanOrEqualTo(1), $"level {level} code {code}");
      }
  }

  [Test]
  [Category("Unit")]
  public void TheQuantiserScaleTablesAreTheOnesTableSevenSixPrints() {
    // The linear column is twice the code all the way up; the non-linear one is the code itself up to
    // eight, then grows in steps of two, four, eight and sixteen.
    for (var code = 1; code <= 31; ++code)
      Assert.That(MpegQuantisation.ScaleOf(code, nonLinear: false), Is.EqualTo(2 * code));

    Assert.That(
      Enumerable.Range(1, 31).Select(code => MpegQuantisation.ScaleOf(code, nonLinear: true)).ToArray(),
      Is.EqualTo(new[] {
        1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 18, 20, 22, 24,
        28, 32, 36, 40, 44, 48, 52, 56, 64, 72, 80, 88, 96, 104, 112,
      }));
  }

  [Test]
  [Category("Unit")]
  public void MismatchControlLeavesEveryBlockSummingToAnOddNumber() {
    // 13818-2 7.4.4. Whatever the block held, its samples add up to an odd number afterwards, and the
    // only coefficient that may have moved is the last one — by exactly one.
    var random = new Random(4711);
    for (var attempt = 0; attempt < 200; ++attempt) {
      var block = new int[64];
      for (var i = 0; i < 64; ++i)
        block[i] = random.Next(-2048, 2048);

      var before = (int[])block.Clone();
      MpegQuantisation.CorrectMismatch(block);

      Assert.That(block.Sum() & 1, Is.EqualTo(1));
      Assert.That(block[..63], Is.EqualTo(before[..63]));
      Assert.That(Math.Abs(block[63] - before[63]), Is.LessThanOrEqualTo(1));
    }
  }

  private static (int Run, int Level)[] _RunsAndLevels(MpegVlcTable table)
    => table.Entries
      .Where(entry => entry.Value >= 0)
      .Select(entry => (Run: MpegVlcTables.RunOf(entry.Value), Level: MpegVlcTables.LevelOf(entry.Value)))
      .ToArray();
}
