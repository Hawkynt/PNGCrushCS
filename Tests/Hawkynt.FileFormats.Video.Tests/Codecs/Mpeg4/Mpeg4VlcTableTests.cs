using System;
using System.Collections.Generic;
using System.Linq;

namespace FileFormat.Codecs.Mpeg4.Tests;

/// <summary>
/// The variable-length code tables of ISO/IEC 14496-2 Annex B, checked as codes rather than through
/// anything they decode.
/// </summary>
/// <remarks>
/// These tables hold four hundred-odd codes transcribed from the standard, and one wrong bit is
/// invisible until some picture somewhere is subtly wrong. What can be checked without a stream is
/// the shape: how many codes each table has, that no code is a prefix of another, and that the values
/// they carry cover exactly the range the standard defines with no gap and no repeat.
/// <para/>
/// The strongest check here is the last one. The standard prints the largest level for every run and
/// the largest run for every level as tables of their own — B-19 to B-22 — and the escape forms are
/// defined in terms of them. Those numbers are an independent statement of the same hundred and two
/// rows, so comparing them against the coefficient tables catches a transcription slip anywhere in
/// either table, which counting and prefix-freeness cannot.
/// </remarks>
[TestFixture]
public sealed class Mpeg4VlcTableTests {

  private static IEnumerable<TestCaseData> _Tables() {
    yield return new TestCaseData("B-3", 3).SetName("Table B-3 modb");
    yield return new TestCaseData("B-4", 4).SetName("Table B-4 mb_type for B-VOPs");
    yield return new TestCaseData("6-28", 3).SetName("Table 6-28 dbquant");
    yield return new TestCaseData("B-6", 9).SetName("Table B-6 MCBPC for I-VOPs");
    yield return new TestCaseData("B-7", 21).SetName("Table B-7 MCBPC for P-VOPs");
    yield return new TestCaseData("B-8", 16).SetName("Table B-8 CBPY");
    yield return new TestCaseData("B-12", 65).SetName("Table B-12 motion vector difference");
    yield return new TestCaseData("B-13", 13).SetName("Table B-13 dct_dc_size_luminance");
    yield return new TestCaseData("B-14", 13).SetName("Table B-14 dct_dc_size_chrominance");
    yield return new TestCaseData("B-16", 103).SetName("Table B-16 intra TCOEF");
    yield return new TestCaseData("B-17", 103).SetName("Table B-17 inter TCOEF");
  }

  private static Mpeg4VlcTable _Table(string number) => number switch {
    "B-3" => Mpeg4VlcTables.BidirectionalMode,
    "B-4" => Mpeg4VlcTables.BidirectionalMacroblockType,
    "6-28" => Mpeg4VlcTables.BidirectionalQuantiserDifference,
    "B-6" => Mpeg4VlcTables.IntraMacroblockType,
    "B-7" => Mpeg4VlcTables.PredictedMacroblockType,
    "B-8" => Mpeg4VlcTables.LuminancePattern,
    "B-12" => Mpeg4VlcTables.MotionVectorDifference,
    "B-13" => Mpeg4VlcTables.LuminanceDcSize,
    "B-14" => Mpeg4VlcTables.ChrominanceDcSize,
    "B-16" => Mpeg4VlcTables.IntraCoefficient,
    _ => Mpeg4VlcTables.InterCoefficient,
  };

  [TestCaseSource(nameof(_Tables))]
  [Category("Unit")]
  public void TheTableHoldsAsManyCodesAsTheStandardPrints(string number, int expected)
    => Assert.That(_Table(number).Entries.Count, Is.EqualTo(expected));

  [TestCaseSource(nameof(_Tables))]
  [Category("Unit")]
  public void NoCodeInTheTableIsAPrefixOfAnother(string number, int expected) {
    _ = expected;
    var codes = _Table(number).Entries.Select(entry => entry.Code.Replace(" ", string.Empty)).ToArray();

    foreach (var code in codes)
      foreach (var other in codes)
        Assert.That(
          ReferenceEquals(code, other) || !other.StartsWith(code, StringComparison.Ordinal), Is.True,
          $"'{code}' is a prefix of '{other}'");
  }

  [TestCaseSource(nameof(_Tables))]
  [Category("Unit")]
  public void EveryCodeAndEveryValueInTheTableIsUnique(string number, int expected) {
    _ = expected;
    var entries = _Table(number).Entries;

    Assert.That(entries.Select(e => e.Code.Replace(" ", string.Empty)).Distinct().Count(), Is.EqualTo(entries.Count));
    Assert.That(entries.Select(e => e.Value).Distinct().Count(), Is.EqualTo(entries.Count));
  }

  [Test]
  [Category("Unit")]
  public void TheMotionVectorTableCoversEveryHalfSampleOfItsRange() {
    // The standard prints the values in whole and half samples from -16 to +16 and says the coded
    // value is twice what it prints, so the range here is -32 to +32 in half-sample units with no
    // gap. Sixty-five values and not sixty-four: the code for +16 whole samples is the one ITU-T
    // H.263's otherwise identical table does not have.
    var values = Mpeg4VlcTables.MotionVectorDifference.Entries.Select(e => e.Value).OrderBy(x => x).ToArray();

    Assert.That(values, Is.EqualTo(Enumerable.Range(-32, 65).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void TheLuminancePatternTableCoversAllSixteenPatterns()
    => Assert.That(
      Mpeg4VlcTables.LuminancePattern.Entries.Select(e => e.Value).OrderBy(x => x).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 16).ToArray()));

  [TestCase("B-13")]
  [TestCase("B-14")]
  [Category("Unit")]
  public void TheDcSizeTableCoversEverySizeFromNoughtToTwelve(string number)
    => Assert.That(
      _Table(number).Entries.Select(e => e.Value).OrderBy(x => x).ToArray(),
      Is.EqualTo(Enumerable.Range(0, 13).ToArray()));

  [Test]
  [Category("Unit")]
  public void TheIntraMacroblockTypesAreTheTwoThatAnIntraPictureCanHold() {
    var types = Mpeg4VlcTables.IntraMacroblockType.Entries
      .Select(e => e.Value)
      .Where(value => value != Mpeg4VlcTables.McbpcStuffing)
      .Select(Mpeg4VlcTables.TypeOf)
      .Distinct()
      .OrderBy(x => x)
      .ToArray();

    Assert.That(types, Is.EqualTo(new[] { 3, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void ThePredictedMacroblockTypesAreTheFiveOfTableSevenAndNoMore() {
    // Five and not six. ITU-T H.263's otherwise identical table carries a sixth type for its own
    // Annex F; MPEG-4 does not, and a decoder that transcribed H.263's table here would accept four
    // codes this standard does not define.
    var types = Mpeg4VlcTables.PredictedMacroblockType.Entries
      .Select(e => e.Value)
      .Where(value => value != Mpeg4VlcTables.McbpcStuffing)
      .Select(Mpeg4VlcTables.TypeOf)
      .Distinct()
      .OrderBy(x => x)
      .ToArray();

    Assert.That(types, Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void BothMacroblockTablesUseTheSameStuffingCode() {
    var intra = Mpeg4VlcTables.IntraMacroblockType.Entries.Single(e => e.Value == Mpeg4VlcTables.McbpcStuffing).Code;
    var predicted = Mpeg4VlcTables.PredictedMacroblockType.Entries
      .Single(e => e.Value == Mpeg4VlcTables.McbpcStuffing).Code;

    Assert.That(predicted.Replace(" ", string.Empty), Is.EqualTo(intra.Replace(" ", string.Empty)));
  }

  [Test]
  [Category("Unit")]
  public void BothCoefficientTablesUseTheSameCodewords() {
    // The standard's two coefficient tables spend the same hundred and two codewords on different
    // events. If one of them had a codeword the other did not, one of the two was mistranscribed.
    var intra = Mpeg4VlcTables.IntraCoefficient.Entries.Select(e => e.Code.Replace(" ", string.Empty)).ToHashSet();
    var inter = Mpeg4VlcTables.InterCoefficient.Entries.Select(e => e.Code.Replace(" ", string.Empty)).ToHashSet();

    Assert.That(intra.SetEquals(inter), Is.True);
  }

  [TestCase(true)]
  [TestCase(false)]
  [Category("Unit")]
  public void EveryCoefficientTripleIsDifferent(bool intra) {
    var seen = new HashSet<(bool, int, int)>();
    for (var index = 0; index < Mpeg4VlcTables.CoefficientEscape; ++index) {
      var last = intra ? Mpeg4VlcTables.IntraIsLast[index] : Mpeg4VlcTables.InterIsLast[index];
      var run = intra ? Mpeg4VlcTables.IntraRun[index] : Mpeg4VlcTables.InterRun[index];
      var level = intra ? Mpeg4VlcTables.IntraLevel[index] : Mpeg4VlcTables.InterLevel[index];

      Assert.That(seen.Add((last, run, level)), Is.True, $"row {index} repeats a (LAST, RUN, LEVEL) triple");
      Assert.That(level, Is.GreaterThan((byte)0), $"row {index} carries a level of zero");
    }
  }

  // ============================================================================================
  // The escape bounds — Tables B-19 to B-22
  // ============================================================================================

  /// <summary>
  /// Table B-19: the largest level the intra table holds for each run, as the standard prints it.
  /// </summary>
  /// <remarks>
  /// Transcribed here and derived from the coefficient table in the decoder, on purpose. The standard
  /// states the same hundred and two rows twice, once as codes and once as these bounds, and a
  /// transcription slip in either shows up as a disagreement between them. A decoder that
  /// transcribed both would have two things that could be wrong together; one that derives one from
  /// the other has a check instead.
  /// </remarks>
  private static IEnumerable<TestCaseData> _LargestLevels() {
    yield return new TestCaseData(true, false, new[] { 27, 10, 5, 4, 3, 3, 3, 3, 2, 2, 1, 1, 1, 1, 1 })
      .SetName("Table B-19, intra, not the last coefficient");
    yield return new TestCaseData(true, true, new[] { 8, 3, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 })
      .SetName("Table B-19, intra, the last coefficient");
    yield return new TestCaseData(
        false, false, new[] { 12, 6, 4, 3, 3, 3, 3, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 })
      .SetName("Table B-20, inter, not the last coefficient");
    yield return new TestCaseData(
        false, true, new[] {
          3, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
          1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        })
      .SetName("Table B-20, inter, the last coefficient");
  }

  [TestCaseSource(nameof(_LargestLevels))]
  [Category("Unit")]
  public void TheLargestLevelForEachRunIsWhatTheStandardPrints(bool intra, bool last, int[] expected) {
    for (var run = 0; run < expected.Length; ++run)
      Assert.That(Mpeg4VlcTables.LargestLevel(intra, last, run), Is.EqualTo(expected[run]), $"run {run}");

    // …and there is nothing past the end of what the standard prints.
    Assert.That(Mpeg4VlcTables.LargestLevel(intra, last, expected.Length), Is.Zero);
  }

  /// <summary>Tables B-21 and B-22: the largest run each table holds for a level.</summary>
  private static IEnumerable<TestCaseData> _LargestRuns() {
    yield return new TestCaseData(true, false, new[] { 14, 9, 7, 3, 2, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })
      .SetName("Table B-21, intra, not the last coefficient");
    yield return new TestCaseData(true, true, new[] { 20, 6, 1, 0, 0, 0, 0, 0 })
      .SetName("Table B-21, intra, the last coefficient");
    yield return new TestCaseData(false, false, new[] { 26, 10, 6, 2, 1, 1, 0, 0, 0, 0, 0, 0 })
      .SetName("Table B-22, inter, not the last coefficient");
    yield return new TestCaseData(false, true, new[] { 40, 1, 0 })
      .SetName("Table B-22, inter, the last coefficient");
  }

  [TestCaseSource(nameof(_LargestRuns))]
  [Category("Unit")]
  public void TheLargestRunForEachLevelIsWhatTheStandardPrints(bool intra, bool last, int[] expected) {
    for (var level = 1; level <= expected.Length; ++level)
      Assert.That(Mpeg4VlcTables.LargestRun(intra, last, level), Is.EqualTo(expected[level - 1]), $"level {level}");

    Assert.That(Mpeg4VlcTables.LargestRun(intra, last, expected.Length + 1), Is.EqualTo(-1));
  }
}
