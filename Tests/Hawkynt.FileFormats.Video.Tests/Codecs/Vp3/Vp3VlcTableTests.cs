using System;
using System.Collections.Generic;
using System.Linq;

namespace FileFormat.Codecs.Vp3.Tests;

/// <summary>
/// The variable-length code tables, and the eighty DCT token codebooks built on them.
/// </summary>
/// <remarks>
/// The codebooks are transcribed rather than derived, so the tests here are about what construction
/// can prove. Every one of the eighty is full and prefix-free — <see cref="Vp3VlcTable"/> refuses
/// anything else at construction, so merely touching the tables proves that much — and every one
/// names all thirty-two tokens, which is checked by decoding every bit pattern a code can be and
/// collecting what comes back.
/// </remarks>
[TestFixture]
public sealed class Vp3VlcTableTests {

  /// <summary>The longest code in any of VP3's tables, so this many bits reach every leaf.</summary>
  private const int _WIDEST_CODE = 15;

  private static Vp3BitReader _Reader(int pattern, int bits) {
    // Left-aligned in as many bytes as it takes, because the reader takes the top bit of the first
    // byte first.
    var bytes = new byte[(bits + 7) / 8 + 1];
    for (var i = 0; i < bits; ++i)
      if ((pattern >> (bits - 1 - i) & 1) != 0)
        bytes[i >> 3] |= (byte)(0x80 >> (i & 7));

    return new(bytes);
  }

  [Test]
  [Category("Unit")]
  public void ThereAreEightyDctTokenCodebooks()
    // Sixteen for the DC position and sixteen for each of the four alternating-current position
    // groups of Table 7.42.
    => Assert.That(Vp3HuffmanTables.All.Length, Is.EqualTo(Vp3HuffmanTables.COUNT).And.EqualTo(80));

  [Test]
  [Category("Unit")]
  public void EveryDctCodebookNamesAllThirtyTwoTokensAndNothingElse() {
    // Decoding every fifteen-bit pattern reaches every leaf of the tree, because no VP3 code is
    // longer than that. What comes back has to be exactly the thirty-two token values: a codebook
    // that named thirty-one of them would still be full and prefix-free, so this is the check that
    // a transcribed table has not lost a token to a duplicate.
    for (var index = 0; index < Vp3HuffmanTables.All.Length; ++index) {
      var seen = new HashSet<int>();
      for (var pattern = 0; pattern < 1 << _WIDEST_CODE; ++pattern)
        seen.Add(Vp3HuffmanTables.All[index].Read(_Reader(pattern, _WIDEST_CODE)));

      Assert.That(seen.OrderBy(token => token), Is.EqualTo(Enumerable.Range(0, 32)),
        $"codebook {index} does not name all thirty-two tokens");
    }
  }

  [Test]
  [Category("Unit")]
  public void EveryMotionVectorMagnitudeAndItsNegationAreBothCodedOnce() {
    // Table 7.23 codes minus thirty-one to thirty-one, with a value and its negation differing in
    // their last bit and nothing else. Zero appears once, not twice.
    var seen = new HashSet<int>();
    for (var pattern = 0; pattern < 1 << 8; ++pattern)
      seen.Add(Vp3Tables.MotionVectorComponents.Read(_Reader(pattern, 8)));

    Assert.That(seen.OrderBy(value => value), Is.EqualTo(Enumerable.Range(-31, 63)));
  }

  [TestCase("000", 0)]
  [TestCase("001", 1)]
  [TestCase("010", -1)]
  [TestCase("0110", 2)]
  [TestCase("1001", -3)]
  [TestCase("101000", 4)]
  [TestCase("101111", -7)]
  [TestCase("1100000", 8)]
  [TestCase("1101111", -15)]
  [TestCase("11100000", 16)]
  [TestCase("11111111", -31)]
  [Category("Unit")]
  public void AMotionVectorCodeReadsBackAsTable723Prints(string code, int expected) {
    // Spot checks straight off the printed table, including both ends of every group.
    var pattern = Convert.ToInt32(code, 2);
    Assert.That(Vp3Tables.MotionVectorComponents.Read(_Reader(pattern, code.Length)), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void ACodeThatIsAPrefixOfAnotherIsRefused() {
    // The two orders are different code paths — one walks into a leaf, the other lands on a taken
    // slot — so both are checked.
    var forwards = Assert.Throws<ArgumentException>(
      () => new Vp3VlcTable("test", ("0", 0), ("00", 1), ("01", 2), ("1", 3)));
    Assert.That(forwards!.Message, Does.Contain("prefix code"));

    var backwards = Assert.Throws<ArgumentException>(
      () => new Vp3VlcTable("test", ("00", 1), ("01", 2), ("0", 0), ("1", 3)));
    Assert.That(backwards!.Message, Does.Contain("prefix code"));
  }

  [Test]
  [Category("Unit")]
  public void ASetOfCodesThatDoesNotFillItsTreeIsRefused() {
    // b11 is missing, so a reader that saw two ones would fall off the end of the table. Every VP3
    // codebook is full, so a transcription that dropped a code has to be caught here.
    var failure = Assert.Throws<ArgumentException>(
      () => new Vp3VlcTable("test", ("0", 0), ("10", 1)));
    Assert.That(failure!.Message, Does.Contain("do not fill their tree"));
  }

  [Test]
  [Category("Unit")]
  public void ATableWrittenAsTextReadsBackTheSameAsOneWrittenAsPairs() {
    // The eighty codebooks are written as text so they can be compared against the printed appendix
    // line by line; this is the check that the text is parsed into what it says.
    var text = new Vp3VlcTable("text", "0:7 10:-3 110:0 111:31");
    var pairs = new Vp3VlcTable("pairs", ("0", 7), ("10", -3), ("110", 0), ("111", 31));

    foreach (var (code, expected) in new[] { ("0", 7), ("10", -3), ("110", 0), ("111", 31) }) {
      var pattern = Convert.ToInt32(code, 2);
      Assert.That(text.Read(_Reader(pattern, code.Length)), Is.EqualTo(expected));
      Assert.That(pairs.Read(_Reader(pattern, code.Length)), Is.EqualTo(expected));
    }
  }
}
