using System.Linq;

namespace FileFormat.Codecs.Vp8.Tests;

/// <summary>
/// The constant tables of RFC 6386, checked for the shape a wrong transcription would break.
/// </summary>
/// <remarks>
/// Three thousand-odd numbers copied out of a printed standard. What can be checked without decoding
/// anything is the shape: how many numbers each table holds, that the values lie in the range the
/// field they fill can hold, and — for the two quantiser tables — that they rise, which the standard
/// prints them doing and a dropped or duplicated line would break.
/// <para/>
/// What that cannot catch is a number that is in range and wrong. That is what comparing whole
/// decoded frames against a reference decoder is for, and it is how these tables were verified.
/// </remarks>
[TestFixture]
public sealed class Vp8TablesTests {

  [Test]
  [Category("Unit")]
  public void TheKeyFrameSubblockModeProbabilitiesAreTenByTenByNine()
    // Indexed by the modes above and to the left, and by the interior nodes of the subblock mode
    // tree (RFC 6386, 11.5).
    => Assert.That(Vp8Tables.KeyFrameSubblockModeProbabilities.Length, Is.EqualTo(10 * 10 * 9));

  [Test]
  [Category("Unit")]
  public void TheTokenProbabilityTablesAreFourByEightByThreeByEleven() {
    // The plane, the coefficient band, how busy the neighbours are, and the interior nodes of the
    // token tree (RFC 6386, 13.3).
    const int expected = 4 * 8 * 3 * (Vp8Trees.TOKEN_COUNT - 1);
    Assert.That(Vp8Tables.DefaultCoefficientProbabilities.Length, Is.EqualTo(expected));
    Assert.That(Vp8Tables.CoefficientUpdateProbabilities.Length, Is.EqualTo(expected));
    Assert.That(Vp8Entropy.COEFFICIENT_PROBABILITY_COUNT, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void EveryProbabilityIsUsable() {
    // A probability of zero would make one branch of a tree free and the other unreachable, which is
    // not a thing an encoder can mean; RFC 6386 section 17.2 goes as far as spelling a written zero
    // as a one for exactly that reason.
    Assert.That(Vp8Tables.KeyFrameSubblockModeProbabilities.Min(), Is.GreaterThan(0));
    Assert.That(Vp8Tables.DefaultCoefficientProbabilities.Min(), Is.GreaterThan(0));
    Assert.That(Vp8Tables.CoefficientUpdateProbabilities.Min(), Is.GreaterThan(0));
    Assert.That(Vp8Trees.SubblockMotionVectorProbabilities.ToArray().Min(), Is.GreaterThan(0));
    Assert.That(Vp8Trees.MotionVectorReferenceProbabilities.ToArray().Min(), Is.GreaterThan(0));
    Assert.That(Vp8Trees.DefaultMotionVectorProbabilities.ToArray().Min(), Is.GreaterThan(0));
    Assert.That(Vp8Trees.CategoryProbabilities.ToArray().Min(), Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void TheQuantiserTablesHaveOneEntryPerIndexAndDoNotFall() {
    // Seven-bit indices, so 128 entries each, and both tables rise across their range (RFC 6386,
    // 14.1). A dropped line would shorten the table; a line out of order would break the rise.
    Assert.That(Vp8Tables.DcQuantiser.Length, Is.EqualTo(128));
    Assert.That(Vp8Tables.AcQuantiser.Length, Is.EqualTo(128));

    for (var index = 1; index < 128; ++index) {
      Assert.That(Vp8Tables.DcQuantiser[index], Is.GreaterThanOrEqualTo(Vp8Tables.DcQuantiser[index - 1]),
        $"the direct current table falls at index {index}");
      Assert.That(Vp8Tables.AcQuantiser[index], Is.GreaterThan(Vp8Tables.AcQuantiser[index - 1]),
        $"the alternating current table does not rise at index {index}");
    }

    // The four corners, as the standard prints them.
    Assert.That(Vp8Tables.DcQuantiser[0], Is.EqualTo(4));
    Assert.That(Vp8Tables.DcQuantiser[127], Is.EqualTo(157));
    Assert.That(Vp8Tables.AcQuantiser[0], Is.EqualTo(4));
    Assert.That(Vp8Tables.AcQuantiser[127], Is.EqualTo(284));
  }

  [Test]
  [Category("Unit")]
  public void TheRangeTokensCoverTheValuesFromFiveUpwardsWithoutAGap() {
    // Each range token covers twice as many values as it has extra bits, and the ranges follow one
    // another from five (RFC 6386, 13.2). A wrong base or a wrong bit count leaves a gap or an
    // overlap, and either one misreads every coefficient past the fifth.
    var next = 5;
    for (var category = 0; category < 6; ++category) {
      Assert.That(Vp8Trees.CategoryBase[category], Is.EqualTo(next), $"category {category + 1} starts in the wrong place");
      next += 1 << Vp8Trees.CategoryBits[category];
    }

    // The last range is wider than the format uses. RFC 6386 gives its span as 67 to 2048, and eleven
    // extra bits reach 2114 — the tail is simply unreachable in a stream any encoder writes.
    Assert.That(Vp8Trees.CategoryBase[5], Is.EqualTo(67));
    Assert.That(Vp8Trees.CategoryBits[5], Is.EqualTo(11));
    Assert.That(next, Is.GreaterThan(2048));
  }

  [Test]
  [Category("Unit")]
  public void EachRangeTokenHasOneExtraBitProbabilityPerExtraBit() {
    var at = 0;
    for (var category = 0; category < 6; ++category) {
      Assert.That(Vp8Trees.CategoryProbabilityOffset[category], Is.EqualTo(at), $"category {category + 1}");
      at += Vp8Trees.CategoryBits[category];
    }

    Assert.That(Vp8Trees.CategoryProbabilities.Length, Is.EqualTo(at));
  }

  [Test]
  [Category("Unit")]
  public void TheMotionVectorProbabilitiesAreNineteenPerComponent() {
    // Whether the magnitude is small, its sign, seven for the tree the small ones use, and ten for
    // the bits the large ones spell out (RFC 6386, 17.1).
    Assert.That(Vp8Trees.MV_PROBABILITY_COUNT, Is.EqualTo(19));
    Assert.That(Vp8Trees.DefaultMotionVectorProbabilities.Length, Is.EqualTo(2 * 19));
    Assert.That(Vp8Trees.MotionVectorUpdateProbabilities.Length, Is.EqualTo(2 * 19));
  }

  [Test]
  [Category("Unit")]
  public void TheCoefficientBandsMapTheSixteenPositionsIntoEightBands() {
    Assert.That(Vp8Trees.CoefficientBands.Length, Is.EqualTo(16));
    Assert.That(Vp8Trees.CoefficientBands.ToArray().Max(), Is.EqualTo(7));
    Assert.That(Vp8Trees.CoefficientBands[0], Is.Zero, "the first coefficient has a band to itself");
    Assert.That(Vp8Trees.CoefficientBands.ToArray().Count(b => b == 0), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void TheProbabilityOffsetAddressesEveryEntryOfTheTokenTableOnce() {
    var seen = new bool[Vp8Entropy.COEFFICIENT_PROBABILITY_COUNT];
    for (var plane = 0; plane < 4; ++plane)
      for (var band = 0; band < 8; ++band)
        for (var context = 0; context < 3; ++context) {
          var at = Vp8Tables.CoefficientProbabilityOffset(plane, band, context);
          for (var node = 0; node < Vp8Trees.TOKEN_COUNT - 1; ++node) {
            Assert.That(seen[at + node], Is.False, $"entry {at + node} is addressed twice");
            seen[at + node] = true;
          }
        }

    Assert.That(seen.All(s => s), Is.True, "some entry of the table is never addressed");
  }
}
