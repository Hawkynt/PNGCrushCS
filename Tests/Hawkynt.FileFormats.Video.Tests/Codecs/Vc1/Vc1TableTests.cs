using System;
using System.Collections.Generic;
using FileFormat.Codecs.Vc1;

namespace FileFormat.Codecs.Vc1.Tests;

/// <summary>
/// The code tables and scan arrays of SMPTE 421M, checked for the properties a transcription slip
/// breaks.
/// </summary>
/// <remarks>
/// There is no reference decoder to compare a table against entry by entry, and eight coding sets of
/// up to a hundred and eighty-six entries is far past what re-reading catches. What can be asserted is
/// what a code is: prefix-free, and — for all but the two Mid Rate tables — complete, in the sense
/// that its codewords fill the space exactly. A single number read from the wrong column of a page
/// shifts every entry after it, which breaks both properties at once.
/// </remarks>
[TestFixture]
public sealed class Vc1TableTests {

  /// <summary>The eight sets by name, since a public test method cannot take an internal type.</summary>
  private static readonly string[] _Names = [
    "HighMotionIntra", "HighMotionInter", "LowMotionIntra", "LowMotionInter",
    "MidRateIntra", "MidRateInter", "HighRateIntra", "HighRateInter",
  ];

  private static Vc1AcCodingSet _Set(string name) => name switch {
    "HighMotionIntra" => Vc1AcCodingSet.HighMotionIntra,
    "HighMotionInter" => Vc1AcCodingSet.HighMotionInter,
    "LowMotionIntra" => Vc1AcCodingSet.LowMotionIntra,
    "LowMotionInter" => Vc1AcCodingSet.LowMotionInter,
    "MidRateIntra" => Vc1AcCodingSet.MidRateIntra,
    "MidRateInter" => Vc1AcCodingSet.MidRateInter,
    "HighRateIntra" => Vc1AcCodingSet.HighRateIntra,
    _ => Vc1AcCodingSet.HighRateInter,
  };

  [TestCaseSource(nameof(_Names))]
  [Category("Unit")]
  public void EveryCodingSetIsInternallyConsistent(string name) {
    var set = _Set(name);

    Assert.Multiple(() => {
      // The code table indexes the run and level tables directly and escapes one index past their end,
      // so the three sizes are one fact stated three times.
      Assert.That(set.Runs, Has.Length.EqualTo(set.EscapeIndex), $"{set.Name}: run table");
      Assert.That(set.Levels, Has.Length.EqualTo(set.EscapeIndex), $"{set.Name}: level table");
      Assert.That(set.Codes.Count, Is.EqualTo(set.EscapeIndex + 1), $"{set.Name}: code table");

      // The last-coefficient pairs are the tail of the index space, so the split has to fall inside it.
      Assert.That(set.StartOfLast, Is.GreaterThan(0).And.LessThan(set.EscapeIndex), $"{set.Name}: start of last");

      // A level of nought is not a coefficient, so the delta run tables are indexed from one and their
      // first slot is padding.
      Assert.That(set.NotLastDeltaRun, Has.Length.GreaterThan(1), $"{set.Name}: not-last delta run");
      Assert.That(set.LastDeltaRun, Has.Length.GreaterThan(1), $"{set.Name}: last delta run");
    });
  }

  [TestCaseSource(nameof(_Names))]
  [Category("Unit")]
  public void EveryCodingSetsCodeTableIsACompletePrefixCode(string name) {
    var set = _Set(name);

    // Building the table already refuses a pair of codes where one prefixes the other. What is left to
    // assert is that nothing is missing: a complete code leaves no cell of the lookup unwritten, and a
    // dropped row shows up here as the space it would have filled.
    //
    // The two Mid Rate tables are the document's own exception and have a test of their own below.
    var expected = name is "MidRateIntra" or "MidRateInter" ? 1 << (set.Codes.MaxLength - 9) : 0;

    Assert.That(set.Codes.UnusedCells, Is.EqualTo(expected), $"{set.Name} leaves {set.Codes.UnusedCells} cells unreachable");
  }

  [Test]
  [Category("Unit")]
  public void TheMidRateTablesReserveTheNineZeroCodeword() {
    // The two exceptions, and they are the document's rather than a slip: both Mid Rate tables fall
    // short of a complete code by exactly one nine-bit codeword, which is the one of nine zeroes. The
    // page they are printed on was checked against this.
    Assert.Multiple(() => {
      Assert.That(Vc1AcCodingSet.MidRateIntra.Codes.UnusedCells, Is.EqualTo(1 << (Vc1AcCodingSet.MidRateIntra.Codes.MaxLength - 9)));
      Assert.That(Vc1AcCodingSet.MidRateInter.Codes.UnusedCells, Is.EqualTo(1 << (Vc1AcCodingSet.MidRateInter.Codes.MaxLength - 9)));
    });
  }

  [TestCase(true, true)]
  [TestCase(true, false)]
  [TestCase(false, true)]
  [TestCase(false, false)]
  [Category("Unit")]
  public void EveryDcDifferentialTableIsACompletePrefixCode(bool highMotion, bool luma) {
    var table = Vc1BlockDecoder.DcTable(highMotion, luma);

    Assert.Multiple(() => {
      // A hundred and nineteen differentials and an escape.
      Assert.That(table.Count, Is.EqualTo(120), table.Name);
      Assert.That(table.UnusedCells, Is.Zero, $"{table.Name} leaves {table.UnusedCells} cells unreachable");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheCodedBlockPatternTableNamesEverySixBitPattern() {
    var table = new Vc1VlcTable("I-Picture CBPCY", Vc1Tables.IPictureCbpcy);

    Assert.Multiple(() => {
      Assert.That(table.Count, Is.EqualTo(64));
      Assert.That(table.UnusedCells, Is.Zero);
    });
  }

  [TestCase("normal")]
  [TestCase("horizontal")]
  [TestCase("vertical")]
  [Category("Unit")]
  public void EveryScanVisitsAllSixtyFourPositionsExactlyOnce(string which) {
    var scan = which switch {
      "normal" => Vc1Tables.NormalScan,
      "horizontal" => Vc1Tables.HorizontalScan,
      _ => Vc1Tables.VerticalScan,
    };

    var seen = new bool[64];
    foreach (var position in scan) {
      Assert.That(position, Is.LessThan(64), $"the {which} scan reaches position {position}");
      Assert.That(seen[position], Is.False, $"the {which} scan reaches position {position} twice");
      seen[position] = true;
    }

    Assert.That(scan.Length, Is.EqualTo(64));

    // The DC always lands at the corner, whichever scan is chosen — the standard says so, and it is
    // what lets the DC be written into the array before the AC coefficients are scanned in.
    Assert.That(scan[0], Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void TheThreeScansAreDifferentOrderings() {
    // The two repaired positions differ between the three, so a repair copied from one to another would
    // show up as two of them being the same list.
    Assert.Multiple(() => {
      Assert.That(Vc1Tables.NormalScan.SequenceEqual(Vc1Tables.HorizontalScan), Is.False);
      Assert.That(Vc1Tables.NormalScan.SequenceEqual(Vc1Tables.VerticalScan), Is.False);
      Assert.That(Vc1Tables.HorizontalScan.SequenceEqual(Vc1Tables.VerticalScan), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheCodingSetChosenDependsOnTheQuantiserOnlyAtIndexZero() {
    // Index nought means the High Rate set on a finely quantised picture and the Low Motion set on a
    // coarsely quantised one; the other two indices mean the same set either way. It is the only place
    // in the format where a table index is read against something other than itself.
    Assert.Multiple(() => {
      Assert.That(Vc1AcCodingSet.For(0, luma: true, 8), Is.SameAs(Vc1AcCodingSet.HighRateIntra));
      Assert.That(Vc1AcCodingSet.For(0, luma: true, 9), Is.SameAs(Vc1AcCodingSet.LowMotionIntra));
      Assert.That(Vc1AcCodingSet.For(0, luma: false, 8), Is.SameAs(Vc1AcCodingSet.HighRateInter));
      Assert.That(Vc1AcCodingSet.For(0, luma: false, 9), Is.SameAs(Vc1AcCodingSet.LowMotionInter));

      Assert.That(Vc1AcCodingSet.For(1, luma: true, 5), Is.SameAs(Vc1AcCodingSet.HighMotionIntra));
      Assert.That(Vc1AcCodingSet.For(1, luma: true, 20), Is.SameAs(Vc1AcCodingSet.HighMotionIntra));
      Assert.That(Vc1AcCodingSet.For(2, luma: false, 5), Is.SameAs(Vc1AcCodingSet.MidRateInter));
      Assert.That(Vc1AcCodingSet.For(2, luma: false, 20), Is.SameAs(Vc1AcCodingSet.MidRateInter));
    });
  }
}
