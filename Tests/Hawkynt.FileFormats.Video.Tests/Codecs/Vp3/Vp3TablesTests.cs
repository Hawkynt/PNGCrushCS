using System.Linq;

namespace FileFormat.Codecs.Vp3.Tests;

/// <summary>
/// The constant tables VP3 has built in, checked for the shape a wrong transcription would break.
/// </summary>
/// <remarks>
/// Appendix B of the Theora specification is several hundred numbers and two and a half thousand
/// Huffman codes, all copied out of a printed document. What can be checked without decoding anything
/// is the shape: how many numbers each table holds, that they lie in the range the field they fill
/// can hold, and the properties the tables have by construction — the scales fall as the quantisation
/// index rises, the zig-zag order is a permutation, each mode alphabet is a permutation, and the DC
/// predictor weights sum to their own divisor so the predictor has unity gain.
/// <para/>
/// What none of that catches is a number that is in range, keeps the shape and is wrong. That is what
/// comparing whole decoded frames against a reference decoder is for, and it is how these tables were
/// actually verified.
/// </remarks>
[TestFixture]
public sealed class Vp3TablesTests {

  [Test]
  [Category("Unit")]
  public void EveryTableIndexedByQuantisationIndexHasSixtyFourEntries() {
    // The quantisation index is six bits (Theora 7.1), so every table it indexes has 64 entries.
    Assert.That(Vp3Tables.LoopFilterLimits.Length, Is.EqualTo(64));
    Assert.That(Vp3Tables.AcScale.Length, Is.EqualTo(64));
    Assert.That(Vp3Tables.DcScale.Length, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void TheScalesFallAsTheQuantisationIndexRises() {
    // A higher quantisation index means a finer quantiser, so both scales fall across the range and
    // the loop filter limit with them. A dropped or duplicated line breaks the fall.
    for (var index = 1; index < 64; ++index) {
      Assert.That(Vp3Tables.AcScale[index], Is.LessThan(Vp3Tables.AcScale[index - 1]),
        $"the alternating current scale does not fall at index {index}");
      Assert.That(Vp3Tables.DcScale[index], Is.LessThanOrEqualTo(Vp3Tables.DcScale[index - 1]),
        $"the direct current scale rises at index {index}");
      Assert.That(Vp3Tables.LoopFilterLimits[index], Is.LessThanOrEqualTo(Vp3Tables.LoopFilterLimits[index - 1]),
        $"the loop filter limit rises at index {index}");
    }

    // The four corners, as Appendix B prints them.
    Assert.That(Vp3Tables.AcScale[0], Is.EqualTo(500));
    Assert.That(Vp3Tables.AcScale[63], Is.EqualTo(10));
    Assert.That(Vp3Tables.DcScale[0], Is.EqualTo(220));
    Assert.That(Vp3Tables.DcScale[63], Is.EqualTo(10));
    Assert.That(Vp3Tables.LoopFilterLimits[0], Is.EqualTo(30));
    Assert.That(Vp3Tables.LoopFilterLimits[63], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void TheLoopFilterTurnsItselfOffAtTheFinestQuantisers() {
    // The last sixteen limits are zero, which makes the tapered response zero everywhere and the
    // filter a no-op. That is a real feature of the table and not a gap in it: at those quantisers
    // there is no blocking to remove.
    for (var index = 48; index < 64; ++index)
      Assert.That(Vp3Tables.LoopFilterLimits[index], Is.Zero, $"limit {index}");

    Assert.That(Vp3Tables.LoopFilterLimits[47], Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ThereAreThreeBaseMatricesOfSixtyFourValuesEach() {
    // Luma intra, chroma intra, and one for every plane of an inter block (Appendix B.3).
    Assert.That(Vp3Tables.BaseMatrices.Length, Is.EqualTo(3));
    foreach (var matrix in Vp3Tables.BaseMatrices) {
      Assert.That(matrix.Length, Is.EqualTo(64));
      Assert.That(matrix.Min(), Is.GreaterThan(0));
      Assert.That(matrix.Max(), Is.LessThanOrEqualTo(255), "a base matrix value is an unsigned byte");
    }

    // Every quantisation type and plane names one of the three.
    Assert.That(Vp3Tables.BaseMatrixOf.Length, Is.EqualTo(2));
    foreach (var row in Vp3Tables.BaseMatrixOf) {
      Assert.That(row.Length, Is.EqualTo(3));
      foreach (var index in row)
        Assert.That(index, Is.InRange(0, 2));
    }
  }

  [Test]
  [Category("Unit")]
  public void TheZigZagOrderIsAPermutationOfTheSixtyFourPositions()
    // Figure 2.8. Every natural position maps to a different zig-zag position, or a coefficient would
    // be read twice and another never.
    => Assert.That(Vp3Tables.ZigZag.OrderBy(value => value), Is.EqualTo(Enumerable.Range(0, 64)));

  [Test]
  [Category("Unit")]
  public void TheZigZagOrderStartsAndEndsWhereTheFigureShows() {
    // The DC coefficient first, the two around it next, and the highest frequency last.
    Assert.That(Vp3Tables.ZigZag[0], Is.Zero);
    Assert.That(Vp3Tables.ZigZag[1], Is.EqualTo(1));
    Assert.That(Vp3Tables.ZigZag[8], Is.EqualTo(2));
    Assert.That(Vp3Tables.ZigZag[63], Is.EqualTo(63));
  }

  [Test]
  [Category("Unit")]
  public void TheCosinesAreTheSixteenBitApproximationsOfTableSevenSixtyFive() {
    // Ci is cos(i*pi/16) scaled by 65536, so they fall as i rises and C4 is 65536/sqrt(2).
    Assert.That(Vp3Tables.Cosines.Length, Is.EqualTo(8));
    Assert.That(Vp3Tables.Cosines[4], Is.EqualTo(46341));

    for (var index = 2; index < 8; ++index)
      Assert.That(Vp3Tables.Cosines[index], Is.LessThan(Vp3Tables.Cosines[index - 1]), $"cosine {index}");

    Assert.That(Vp3Tables.Cosines[1], Is.EqualTo(64277));
    Assert.That(Vp3Tables.Cosines[7], Is.EqualTo(12785));
  }

  [Test]
  [Category("Unit")]
  public void EachModeAlphabetIsAPermutationOfTheEightModes() {
    // Schemes one to six differ only in which mode gets which code, so each is a rearrangement of the
    // same eight modes (Table 7.19). A repeated entry would make one mode uncodable.
    Assert.That(Vp3Tables.ModeAlphabets.Length, Is.EqualTo(7));
    Assert.That(Vp3Tables.ModeAlphabets[0], Is.Empty, "scheme zero states its own alphabet");

    for (var scheme = 1; scheme <= 6; ++scheme)
      Assert.That(Vp3Tables.ModeAlphabets[scheme].OrderBy(mode => mode), Is.EqualTo(Enumerable.Range(0, 8)),
        $"scheme {scheme}");
  }

  [Test]
  [Category("Unit")]
  public void TheLastTwoCodesOfEveryModeSchemeNameTheSameTwoModes() {
    // Table 7.19 gives the two longest codes to modes 6 and 7 in every scheme; the schemes only
    // rearrange the six shorter ones.
    for (var scheme = 1; scheme <= 6; ++scheme) {
      Assert.That(Vp3Tables.ModeAlphabets[scheme][6], Is.EqualTo(6), $"scheme {scheme}");
      Assert.That(Vp3Tables.ModeAlphabets[scheme][7], Is.EqualTo(7), $"scheme {scheme}");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheReferenceFrameOfEachModeIsOneOfThree() {
    // None, previous or golden (Table 7.46), and only the intra mode uses none.
    Assert.That(Vp3Tables.ReferenceOfMode.Length, Is.EqualTo(8));
    for (var mode = 0; mode < 8; ++mode)
      Assert.That(Vp3Tables.ReferenceOfMode[mode], Is.InRange(0, 2), $"mode {mode}");

    Assert.That(Vp3Tables.ReferenceOfMode[1], Is.Zero, "INTRA predicts from no reference frame");
    Assert.That(Vp3Tables.ReferenceOfMode[5], Is.EqualTo(2), "INTER GOLDEN NOMV predicts from the golden frame");
    Assert.That(Vp3Tables.ReferenceOfMode[6], Is.EqualTo(2), "INTER GOLDEN MV predicts from the golden frame");
  }

  [Test]
  [Category("Unit")]
  public void EveryDcPredictorHasUnityGain() {
    // Table 7.47 gives four weights and a divisor for each set of usable neighbours. The weights of
    // the neighbours that set says are usable sum to the divisor — so a block whose neighbours all
    // hold the same value is predicted to hold that value, whatever the set. That is the property
    // the extrapolating rows have to obey too, and 29 - 26 + 29 is 32 exactly because of it.
    Assert.That(Vp3Tables.DcPredictorWeights.Length, Is.EqualTo(16));

    for (var pattern = 1; pattern < 16; ++pattern) {
      var row = Vp3Tables.DcPredictorWeights[pattern];
      Assert.That(row.Length, Is.EqualTo(5), $"pattern {pattern}");

      var sum = 0;
      for (var neighbour = 0; neighbour < 4; ++neighbour) {
        if ((pattern & (1 << neighbour)) != 0)
          sum += row[neighbour];
        else
          Assert.That(row[neighbour], Is.Zero,
            $"pattern {pattern} weights a neighbour it does not have");
      }

      Assert.That(sum, Is.EqualTo(row[4]), $"pattern {pattern} does not have unity gain");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheRunLengthTablesCoverTheRangesTheSpecificationPrints() {
    // Table 7.7 runs from one to 4129 with no gaps: each code's first length is one past the last
    // length the previous code reached. Table 7.11 does the same from one to thirty.
    Assert.That(Vp3Tables.LongRunStarts.Length, Is.EqualTo(7));
    Assert.That(Vp3Tables.LongRunExtraBits.Length, Is.EqualTo(7));
    Assert.That(Vp3Tables.LongRunStarts[0], Is.EqualTo(1));

    for (var code = 1; code < 7; ++code)
      Assert.That(Vp3Tables.LongRunStarts[code],
        Is.EqualTo(Vp3Tables.LongRunStarts[code - 1] + (1 << Vp3Tables.LongRunExtraBits[code - 1])),
        $"long run code {code} does not continue where {code - 1} stopped");

    var longest = Vp3Tables.LongRunStarts[6] + (1 << Vp3Tables.LongRunExtraBits[6]) - 1;
    Assert.That(longest, Is.EqualTo(Vp3Tables.LONG_RUN_LIMIT));

    Assert.That(Vp3Tables.ShortRunStarts.Length, Is.EqualTo(6));
    Assert.That(Vp3Tables.ShortRunExtraBits.Length, Is.EqualTo(6));
    Assert.That(Vp3Tables.ShortRunStarts[0], Is.EqualTo(1));

    for (var code = 1; code < 6; ++code)
      Assert.That(Vp3Tables.ShortRunStarts[code],
        Is.EqualTo(Vp3Tables.ShortRunStarts[code - 1] + (1 << Vp3Tables.ShortRunExtraBits[code - 1])),
        $"short run code {code} does not continue where {code - 1} stopped");

    Assert.That(Vp3Tables.ShortRunStarts[5] + (1 << Vp3Tables.ShortRunExtraBits[5]) - 1, Is.EqualTo(30));
  }
}
