using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.MsMpeg4.Tests;

/// <summary>
/// The tables Microsoft MPEG-4 version 2 reads, checked against what they are supposed to be.
/// </summary>
/// <remarks>
/// Two kinds of check, because the tables come from two places. The two Microsoft invented were
/// derived from the bitstream and are written out in the decoder, so what can be checked about them is
/// their shape: that they are prefix codes, and — for the macroblock types — that they are
/// <i>complete</i> prefix codes over all eight values, which is Kraft's equality and is the one
/// property that says no codeword was missed. A table with a value left out would decode every stream
/// an encoder produces and fail on the first file that used the missing one.
/// <para/>
/// The rest are ISO/IEC 14496-2's, reached by a transformation, and what is checked there is the
/// transformation: that the DC tables really are the standard's with every bit inverted, and that the
/// motion vector magnitudes really are the standard's codes with the sign bit removed.
/// </remarks>
[TestFixture]
public sealed class MsMpeg4V2TableTests {

  private static IReadOnlyList<(string Code, int Value)> _Entries(Mpeg4VlcTable table) => table.Entries;

  private static string _Bits(string code) => code.Replace(" ", string.Empty);

  private static void _AssertPrefixFree(Mpeg4VlcTable table) {
    var codes = _Entries(table).Select(e => _Bits(e.Code)).ToArray();
    foreach (var a in codes)
      foreach (var b in codes) {
        if (ReferenceEquals(a, b))
          continue;

        Assert.That(b.StartsWith(a, StringComparison.Ordinal) && a != b, Is.False,
                    $"'{a}' is a prefix of '{b}' in {table.Name}.");
      }
  }

  /// <summary>Kraft's sum over a table's codeword lengths; exactly one means a complete prefix code.</summary>
  private static double _Kraft(Mpeg4VlcTable table)
    => _Entries(table).Sum(e => Math.Pow(0.5, _Bits(e.Code).Length));

  [Test]
  [Category("Unit")]
  public void TheMacroblockTypesAreACompletePrefixCodeOverAllEightValues() {
    var table = MsMpeg4VlcTables.MacroblockType;

    Assert.Multiple(() => {
      Assert.That(_Entries(table).Select(e => e.Value).OrderBy(v => v), Is.EqualTo(Enumerable.Range(0, 8)));
      Assert.That(_Kraft(table), Is.EqualTo(1.0).Within(1e-12));
    });

    _AssertPrefixFree(table);
  }

  [Test]
  [Category("Unit")]
  public void TheIntraChromaPatternsAreACompletePrefixCodeOverAllFourValues() {
    var table = MsMpeg4VlcTables.IntraChromaPattern;

    Assert.Multiple(() => {
      Assert.That(_Entries(table).Select(e => e.Value).OrderBy(v => v), Is.EqualTo(Enumerable.Range(0, 4)));
      Assert.That(_Kraft(table), Is.EqualTo(1.0).Within(1e-12));
    });

    _AssertPrefixFree(table);
  }

  [TestCase(0, false, false)]
  [TestCase(1, false, false)]
  [TestCase(3, false, true)]
  [TestCase(4, true, false)]
  [TestCase(6, true, true)]
  [TestCase(7, true, true)]
  [Category("Unit")]
  public void AMacroblockTypeSplitsIntoAnIntraFlagAndTwoChrominanceBits(int value, bool intra, bool cb) {
    Assert.Multiple(() => {
      Assert.That(MsMpeg4VlcTables.IsIntra(value), Is.EqualTo(intra));
      Assert.That((MsMpeg4VlcTables.ChromaPatternOf(value) & 2) != 0, Is.EqualTo(cb));
      Assert.That(MsMpeg4VlcTables.ChromaPatternOf(value), Is.EqualTo(value & 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheLuminanceDcSizesAreTableB13WithEveryBitInverted()
    => _AssertInverted(Mpeg4VlcTables.LuminanceDcSize, MsMpeg4VlcTables.LuminanceDcSize);

  [Test]
  [Category("Unit")]
  public void TheChrominanceDcSizesAreTableB14WithEveryBitInverted()
    => _AssertInverted(Mpeg4VlcTables.ChrominanceDcSize, MsMpeg4VlcTables.ChrominanceDcSize);

  private static void _AssertInverted(Mpeg4VlcTable standard, Mpeg4VlcTable ours) {
    var mine = _Entries(ours).ToDictionary(e => e.Value, e => _Bits(e.Code));

    Assert.That(mine, Has.Count.EqualTo(_Entries(standard).Count));
    foreach (var (code, value) in _Entries(standard)) {
      var inverted = new string(_Bits(code).Select(c => c == '0' ? '1' : '0').ToArray());
      Assert.That(mine[value], Is.EqualTo(inverted), $"size {value}");
    }
  }

  [Test]
  [Category("Unit")]
  public void ADifferentialOfNoughtIsCodedAsTheStandardsOnesRatherThanItsZeroes() {
    // The one entry worth naming: ISO/IEC 14496-2 writes a luminance size of nought as 011, and this
    // format writes 100. A decoder that took the standard's table unaltered would read the commonest
    // code in a flat picture as a size of four and then take four bits that belong to the next block.
    var zero = _Entries(MsMpeg4VlcTables.LuminanceDcSize).Single(e => e.Value == 0);

    Assert.That(_Bits(zero.Code), Is.EqualTo("100"));
  }

  [Test]
  [Category("Unit")]
  public void TheMotionVectorMagnitudesAreTableB12WithoutItsSignBit() {
    var standard = _Entries(Mpeg4VlcTables.MotionVectorDifference).ToDictionary(e => e.Value, e => _Bits(e.Code));
    var mine = _Entries(MsMpeg4VlcTables.MotionVectorMagnitude).ToDictionary(e => e.Value, e => _Bits(e.Code));

    Assert.That(mine, Has.Count.EqualTo(33));
    Assert.Multiple(() => {
      Assert.That(mine[0], Is.EqualTo(standard[0]), "a difference of nought carries no sign bit");
      for (var magnitude = 1; magnitude <= 32; ++magnitude) {
        Assert.That(standard[magnitude], Is.EqualTo(mine[magnitude] + "0"), $"+{magnitude}");
        Assert.That(standard[-magnitude], Is.EqualTo(mine[magnitude] + "1"), $"-{magnitude}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void TheMotionVectorMagnitudesAreStillAPrefixCodeOnceTheSignIsGone()
    => _AssertPrefixFree(MsMpeg4VlcTables.MotionVectorMagnitude);

  [Test]
  [Category("Unit")]
  public void TheCoefficientTablesAreTheStandardsOwnUnaltered() {
    // Not a transformation at all, and that is the point: an intra luminance block reads Table B-16
    // and everything else reads Table B-17, both exactly as the standard prints them. The split is
    // between the two tables rather than between intra and predicted macroblocks, which is the part
    // the standard would not lead anyone to guess.
    Assert.Multiple(() => {
      Assert.That(Mpeg4VlcTables.IntraCoefficient.Name, Does.Contain("B-16"));
      Assert.That(Mpeg4VlcTables.InterCoefficient.Name, Does.Contain("B-17"));
      Assert.That(_Entries(Mpeg4VlcTables.IntraCoefficient).Single(e => e.Value == 67).Code, Is.EqualTo("0111"));
      Assert.That(_Entries(Mpeg4VlcTables.InterCoefficient).Single(e => e.Value == 58).Code, Is.EqualTo("0111"));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheDcStepIsTheConstantEight()
    => Assert.That(MsMpeg4BlockDecoder.DcStep, Is.EqualTo(8));

  [Test]
  [Category("Unit")]
  public void AnAbsentBlockContributesTheMiddleOfTheRange()
    => Assert.That(MsMpeg4IntraPrediction.AbsentDc, Is.EqualTo((1024 + MsMpeg4BlockDecoder.DcStep / 2) / MsMpeg4BlockDecoder.DcStep));
}
