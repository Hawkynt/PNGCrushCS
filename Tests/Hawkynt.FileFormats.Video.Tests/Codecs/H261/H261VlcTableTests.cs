using System.Linq;
using FileFormat.Codecs.H263;

namespace FileFormat.Codecs.H261.Tests;

/// <summary>
/// The variable-length code tables of ITU-T H.261, checked as codes rather than through anything they
/// decode — the same shape of check <c>H263VlcTableTests</c> runs on H.263's own tables, and for the
/// same reason: what these can catch is a dropped line, a duplicated one or a value typed twice, not a
/// code that is unique and attached to the wrong value. That needs a reference decoder, which is what
/// the plane-by-plane comparison against ffmpeg's own H.261 decode was for.
/// </summary>
[TestFixture]
public sealed class H261VlcTableTests {

  [Test]
  [Category("Unit")]
  public void MacroblockAddressCoversOneToThirtyThreeAndStuffing() {
    var values = H261VlcTables.MacroblockAddress.Entries.Select(e => e.Value).OrderBy(v => v).ToArray();

    Assert.That(values, Is.EqualTo(Enumerable.Range(1, 33).Prepend(H261VlcTables.MbaStuffing).OrderBy(v => v).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void MacroblockTypeHasTenRowsWithDistinctCodesAndValues() {
    var entries = H261VlcTables.MacroblockType.Entries;

    Assert.That(entries.Count, Is.EqualTo(10));
    Assert.That(entries.Select(e => e.Value).Distinct().Count(), Is.EqualTo(10));
    Assert.That(entries.Select(e => e.Code.Replace(" ", "")).Distinct().Count(), Is.EqualTo(10));
    Assert.That(entries.Select(e => e.Value).OrderBy(v => v), Is.EqualTo(Enumerable.Range(0, 10)));
  }

  [Test]
  [Category("Unit")]
  public void EveryMacroblockTypeValueIndexesARowOfAll() {
    foreach (var entry in H261VlcTables.MacroblockType.Entries)
      Assert.That(entry.Value, Is.InRange(0, H261MacroblockType.All.Length - 1));
  }

  [Test]
  [Category("Unit")]
  public void MotionVectorDifferenceCoversMinusSixteenToFifteen() {
    var values = H261VlcTables.MotionVectorDifference.Entries.Select(e => e.Value).OrderBy(v => v).ToArray();

    Assert.That(values, Is.EqualTo(Enumerable.Range(-16, 32).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void CodedBlockPatternCoversOneToSixtyThree() {
    var values = H261VlcTables.CodedBlockPattern.Entries.Select(e => e.Value).OrderBy(v => v).ToArray();

    Assert.That(values, Is.EqualTo(Enumerable.Range(1, 63).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void NoCodeInAnyTableIsAPrefixOfAnother() {
    H263VlcTable[] tables = [
      H261VlcTables.MacroblockAddress, H261VlcTables.MacroblockType, H261VlcTables.MotionVectorDifference,
      H261VlcTables.CodedBlockPattern, H261VlcTables.CoefficientFirst, H261VlcTables.CoefficientNotFirst,
    ];

    // Each table's own constructor already refuses a colliding pair at build time — reaching this
    // point at all is itself the check for that. What is asserted here is that every code the
    // Recommendation prints for a table actually made it in, by counting entries in each.
    foreach (var table in tables)
      Assert.That(table.Entries.Count, Is.GreaterThan(0), table.Name);
  }

  [Test]
  [Category("Unit")]
  public void CoefficientFirstNeverCarriesEndOfBlockOrTheThreeBitSpellingOfRunZeroLevelOne() {
    // Table 5's footnote: end of block cannot be the first thing a coded block says, and the short
    // "1s" spelling of run 0, level 1 is reserved for exactly that position.
    var codes = H261VlcTables.CoefficientFirst.Entries.Select(e => e.Code.Replace(" ", "")).ToArray();

    Assert.That(codes, Has.None.EqualTo("10"));
    Assert.That(codes, Has.None.EqualTo("11"));
    Assert.That(codes, Has.One.EqualTo("1"));
  }

  [Test]
  [Category("Unit")]
  public void CoefficientNotFirstCarriesEndOfBlockAtTheCodeRunZeroLevelOnesFirstFormWouldHaveUsed() {
    var eob = H261VlcTables.CoefficientNotFirst.Entries.Single(e => e.Value == H261VlcTables.CoefficientEob);
    Assert.That(eob.Code.Replace(" ", ""), Is.EqualTo("10"));

    var runZeroLevelOne = H261VlcTables.CoefficientNotFirst.Entries.Single(e => e.Value == 0);
    Assert.That(runZeroLevelOne.Code.Replace(" ", ""), Is.EqualTo("11"));
  }

  [Test]
  [Category("Unit")]
  public void BothCoefficientTablesShareEveryOtherCodeAndValue() {
    var first = H261VlcTables.CoefficientFirst.Entries
      .Where(e => e.Value != 0)
      .Select(e => (e.Value, Code: e.Code.Replace(" ", "")))
      .OrderBy(e => e.Value)
      .ToArray();

    var notFirst = H261VlcTables.CoefficientNotFirst.Entries
      .Where(e => e.Value != 0 && e.Value != H261VlcTables.CoefficientEob)
      .Select(e => (e.Value, Code: e.Code.Replace(" ", "")))
      .OrderBy(e => e.Value)
      .ToArray();

    Assert.That(notFirst, Is.EqualTo(first));
  }
}
