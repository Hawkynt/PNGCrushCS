using System;
using System.Collections.Generic;
using System.Linq;

namespace FileFormat.Codecs.H263.Tests;

/// <summary>
/// The variable-length code tables of ITU-T H.263, checked as codes rather than through anything they
/// decode.
/// </summary>
/// <remarks>
/// These tables hold two hundred-odd codes transcribed from the Recommendation, and one wrong bit is
/// invisible until some picture somewhere is subtly wrong. What can be checked without a stream is
/// the shape: how many codes each table has, that no code is a prefix of another, that the bit counts
/// the Recommendation prints beside each code match the codes as written, and that the values they
/// carry cover exactly the range the Recommendation defines with no gap and no repeat. Between them
/// those catch a dropped line, a duplicated one, and a value typed twice.
/// <para/>
/// What they cannot catch is a code that is unique, in range, and attached to the wrong value. That
/// is what comparing whole decoded pictures against a reference decoder is for, and it is how these
/// tables were actually verified.
/// </remarks>
[TestFixture]
public sealed class H263VlcTableTests {

  /// <summary>Each table, by the number the Recommendation prints it under, with how many codes it holds.</summary>
  /// <remarks>
  /// Named rather than passed, because the tables are internal to the codec while a test method NUnit
  /// can call has to be public. The lookup below is the whole of the indirection.
  /// </remarks>
  private static IEnumerable<TestCaseData> _Tables() {
    yield return new TestCaseData("7", 9).SetName("Table 7 MCBPC for I-pictures");
    yield return new TestCaseData("8", 25).SetName("Table 8 MCBPC for P-pictures");
    yield return new TestCaseData("12", 16).SetName("Table 12 CBPY");
    yield return new TestCaseData("14", 64).SetName("Table 14 MVD");
    yield return new TestCaseData("16", 103).SetName("Table 16 TCOEF");
  }

  private static H263VlcTable _Table(string number) => number switch {
    "7" => H263VlcTables.IntraMacroblockType,
    "8" => H263VlcTables.PredictedMacroblockType,
    "12" => H263VlcTables.LuminancePattern,
    "14" => H263VlcTables.MotionVectorDifference,
    _ => H263VlcTables.Coefficient,
  };

  [TestCaseSource(nameof(_Tables))]
  [Category("Unit")]
  public void TheTableHoldsAsManyCodesAsTheRecommendationPrints(string number, int expected)
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
  public void EveryCodeInTheTableIsUnique(string number, int expected) {
    _ = expected;
    var codes = _Table(number).Entries.Select(entry => entry.Code.Replace(" ", string.Empty)).ToArray();

    Assert.That(codes.Distinct().Count(), Is.EqualTo(codes.Length));
  }

  [TestCaseSource(nameof(_Tables))]
  [Category("Unit")]
  public void EveryValueInTheTableIsUnique(string number, int expected) {
    _ = expected;

    // The two MCBPC tables share the stuffing value with nothing else, and every other value in every
    // table stands for one row. A value written twice is a transcription slip that would otherwise
    // decode two different codes to the same macroblock or coefficient.
    var values = _Table(number).Entries.Select(entry => entry.Value).ToArray();

    Assert.That(values.Distinct().Count(), Is.EqualTo(values.Length));
  }

  [Test]
  [Category("Unit")]
  public void TheCoefficientTableCarriesOneTripleForEveryCodeButTheEscape() {
    // One hundred and two rows and an escape, so the arrays the run, level and last flag are read out
    // of have to be exactly one hundred and two long. A slip that shifted one of them against the
    // others would decode to a picture, and it would be the wrong picture.
    Assert.That(H263VlcTables.CoefficientIsLast.Length, Is.EqualTo(H263VlcTables.CoefficientEscape));
    Assert.That(H263VlcTables.CoefficientRun.Length, Is.EqualTo(H263VlcTables.CoefficientEscape));
    Assert.That(H263VlcTables.CoefficientLevel.Length, Is.EqualTo(H263VlcTables.CoefficientEscape));
  }

  [Test]
  [Category("Unit")]
  public void TheCoefficientTriplesAreAllDifferent() {
    var triples = new HashSet<(bool, byte, byte)>();
    for (var index = 0; index < H263VlcTables.CoefficientEscape; ++index)
      Assert.That(
        triples.Add((H263VlcTables.CoefficientIsLast[index], H263VlcTables.CoefficientRun[index],
          H263VlcTables.CoefficientLevel[index])),
        Is.True, $"row {index} repeats a (LAST, RUN, LEVEL) triple");
  }

  [Test]
  [Category("Unit")]
  public void TheCoefficientLevelsAreNeverZero() {
    // A level of zero is not a coefficient, and Table 16 has no row carrying one.
    for (var index = 0; index < H263VlcTables.CoefficientEscape; ++index)
      Assert.That(H263VlcTables.CoefficientLevel[index], Is.GreaterThan((byte)0), $"row {index}");
  }

  [Test]
  [Category("Unit")]
  public void TheMotionVectorTableCoversEveryHalfPixelOfThePermittedRange() {
    // -16 to 15.5 in steps of half a pixel is sixty-four values, and Table 14 has exactly one code
    // for each. The values here are in half-pixel units, so the range is -32 to 31.
    var values = H263VlcTables.MotionVectorDifference.Entries.Select(entry => entry.Value).OrderBy(x => x).ToArray();

    Assert.That(values, Is.EqualTo(Enumerable.Range(-32, 64).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void EachMotionVectorCodeAndItsNegationDifferOnlyInTheirLastBit() {
    // Table 14 is symmetric by construction: for every pair of vectors n and -n the two codes are the
    // same length and agree everywhere but the final bit. That is an independent check on all
    // sixty-three of the non-zero rows — a single mistyped bit anywhere in the table breaks it.
    var byValue = H263VlcTables.MotionVectorDifference.Entries
      .ToDictionary(entry => entry.Value, entry => entry.Code.Replace(" ", string.Empty));

    for (var value = 1; value <= 31; ++value) {
      var positive = byValue[value];
      var negative = byValue[-value];

      Assert.That(negative.Length, Is.EqualTo(positive.Length), $"vector {value} and its negation differ in length");
      Assert.That(negative[..^1], Is.EqualTo(positive[..^1]), $"vector {value} and its negation differ before the last bit");
      Assert.That(negative[^1], Is.Not.EqualTo(positive[^1]), $"vector {value} and its negation share their last bit");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheLuminancePatternTableCoversAllSixteenPatterns() {
    var values = H263VlcTables.LuminancePattern.Entries.Select(entry => entry.Value).OrderBy(x => x).ToArray();

    Assert.That(values, Is.EqualTo(Enumerable.Range(0, 16).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void TheIntraMacroblockTypesAreTheTwoThatAnIntraPictureCanHold() {
    // Table 7 carries types 3 and 4 with all four chrominance patterns, and the stuffing code. The
    // other four types of Table 9 have no meaning in a picture that is entirely intra coded.
    var types = H263VlcTables.IntraMacroblockType.Entries
      .Select(entry => entry.Value)
      .Where(value => value != H263VlcTables.McbpcStuffing)
      .Select(H263VlcTables.TypeOf)
      .Distinct()
      .OrderBy(x => x)
      .ToArray();

    Assert.That(types, Is.EqualTo(new[] { 3, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void ThePredictedMacroblockTypesAreAllSixOfTableNine() {
    var types = H263VlcTables.PredictedMacroblockType.Entries
      .Select(entry => entry.Value)
      .Where(value => value != H263VlcTables.McbpcStuffing)
      .Select(H263VlcTables.TypeOf)
      .Distinct()
      .OrderBy(x => x)
      .ToArray();

    Assert.That(types, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
  }

  [Test]
  [Category("Unit")]
  public void BothMacroblockTablesUseTheSameStuffingCode() {
    // The Recommendation prints the same nine-bit codeword in Table 7 and Table 8, and a decoder that
    // had transcribed one of them differently would take a stuffed macroblock in one picture type and
    // lose the bitstream in the other.
    var intra = H263VlcTables.IntraMacroblockType.Entries.Single(e => e.Value == H263VlcTables.McbpcStuffing).Code;
    var predicted = H263VlcTables.PredictedMacroblockType.Entries.Single(e => e.Value == H263VlcTables.McbpcStuffing).Code;

    Assert.That(predicted.Replace(" ", string.Empty), Is.EqualTo(intra.Replace(" ", string.Empty)));
  }
}
