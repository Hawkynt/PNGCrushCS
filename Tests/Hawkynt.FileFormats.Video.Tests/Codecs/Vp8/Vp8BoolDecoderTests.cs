using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Vp8.Tests;

/// <summary>
/// The boolean entropy decoder of RFC 6386 section 7, against the encoder the same section defines.
/// </summary>
/// <remarks>
/// The two halves are specified together and are only meaningful together: a decoder is right when
/// it reads back exactly what an encoder wrote, at the same probabilities, in the same order. So the
/// tests here write sequences and read them back, including the awkward ones — a run of very likely
/// values, which the coder compresses to almost nothing, and a run of very unlikely ones, which it
/// spends more than a bit each on.
/// </remarks>
[TestFixture]
public sealed class Vp8BoolDecoderTests {

  [Test]
  [Category("Unit")]
  public void ASequenceOfBoolsReadsBackAsItWasWritten() {
    var random = new Random(20250821);
    var probabilities = new List<int>();
    var values = new List<int>();
    var stream = new Vp8TestStream();

    for (var i = 0; i < 5000; ++i) {
      var probability = 1 + random.Next(255);
      var value = random.Next(2);
      probabilities.Add(probability);
      values.Add(value);
      stream.Bool(probability, value);
    }

    var bytes = stream.Finish();
    var reader = new Vp8BoolDecoder(bytes, 0, bytes.Length);
    for (var i = 0; i < values.Count; ++i)
      Assert.That(reader.ReadBool(probabilities[i]), Is.EqualTo(values[i]), $"bool {i}");
  }

  [TestCase(1)]
  [TestCase(2)]
  [TestCase(128)]
  [TestCase(254)]
  [TestCase(255)]
  [Category("Unit")]
  public void ALongRunAtOneProbabilityReadsBack(int probability) {
    var stream = new Vp8TestStream();
    for (var i = 0; i < 2000; ++i)
      stream.Bool(probability, i % 3 == 0 ? 1 : 0);

    var bytes = stream.Finish();
    var reader = new Vp8BoolDecoder(bytes, 0, bytes.Length);
    for (var i = 0; i < 2000; ++i)
      Assert.That(reader.ReadBool(probability), Is.EqualTo(i % 3 == 0 ? 1 : 0), $"bool {i}");
  }

  [Test]
  [Category("Unit")]
  public void LiteralsReadBackAsTheyWereWritten() {
    var stream = new Vp8TestStream();
    stream.Literal(7, 91).Literal(19, 0x7BCDE).Literal(1, 1).Literal(8, 0).Literal(6, 63);

    var bytes = stream.Finish();
    var reader = new Vp8BoolDecoder(bytes, 0, bytes.Length);
    Assert.That(reader.ReadLiteral(7), Is.EqualTo(91));
    Assert.That(reader.ReadLiteral(19), Is.EqualTo(0x7BCDE));
    Assert.That(reader.ReadLiteral(1), Is.EqualTo(1));
    Assert.That(reader.ReadLiteral(8), Is.Zero);
    Assert.That(reader.ReadLiteral(6), Is.EqualTo(63));
  }

  [Test]
  [Category("Unit")]
  public void ASignedValueIsAMagnitudeFollowedByItsSign() {
    // The sign comes after the magnitude and a set bit means negative, which is the opposite way
    // round from two's complement and from the sign bit on a coefficient (RFC 6386, 9.3 and 9.6).
    var stream = new Vp8TestStream();
    stream.Literal(6, 21).Flag(1); // -21
    stream.Literal(6, 21).Flag(0); // +21
    stream.Literal(4, 0).Flag(1); // negative zero, which is zero

    var bytes = stream.Finish();
    var reader = new Vp8BoolDecoder(bytes, 0, bytes.Length);
    Assert.That(reader.ReadSignedValue(6), Is.EqualTo(-21));
    Assert.That(reader.ReadSignedValue(6), Is.EqualTo(21));
    Assert.That(reader.ReadSignedValue(4), Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void ATreeCodedValueReadsBackAsTheValueTheCodeNames() {
    var stream = new Vp8TestStream();
    var written = new[] {
      stream.Coded(Vp8Trees.Token, Vp8Tables.DefaultCoefficientProbabilities, 0, "1111111"),
      stream.Coded(Vp8Trees.Token, Vp8Tables.DefaultCoefficientProbabilities, 0, "0"),
      stream.Coded(Vp8Trees.Token, Vp8Tables.DefaultCoefficientProbabilities, 0, "11100"),
    };

    Assert.That(written, Is.EqualTo(new[] { Vp8Token.CATEGORY_6, Vp8Token.END_OF_BLOCK, Vp8Token.TWO }));

    var bytes = stream.Finish();
    var reader = new Vp8BoolDecoder(bytes, 0, bytes.Length);
    foreach (var expected in written)
      Assert.That(reader.ReadTree(Vp8Trees.Token, Vp8Tables.DefaultCoefficientProbabilities, 0), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void SkippingTheEndOfBlockBranchStartsTheWalkOneNodeIn() {
    // A token that follows a literal zero cannot be end-of-block, so the branch that would decide it
    // is not written (RFC 6386, 13.2). Reading such a token means starting at node two.
    var stream = new Vp8TestStream();
    var probabilities = Vp8Tables.DefaultCoefficientProbabilities;

    // "10" is a zero, and after it the next token is written without its first bit: "0" alone is a
    // second zero rather than an end of block.
    stream.Coded(Vp8Trees.Token, probabilities, 0, "10");
    stream.Bool(probabilities[1], 0);

    var bytes = stream.Finish();
    var reader = new Vp8BoolDecoder(bytes, 0, bytes.Length);
    Assert.That(reader.ReadTree(Vp8Trees.Token, probabilities, 0), Is.EqualTo(Vp8Token.ZERO));
    Assert.That(
      reader.ReadTree(Vp8Trees.Token, probabilities, 0, Vp8Trees.TOKEN_TREE_WITHOUT_END_OF_BLOCK),
      Is.EqualTo(Vp8Token.ZERO));
  }

  [TestCase(0)]
  [TestCase(1)]
  [Category("Unit")]
  public void APartitionTooShortToPrimeTheWindowStillReads(int length) {
    // A frame whose macroblocks all declare themselves free of coefficients writes nothing into its
    // token partitions, and encoders duly leave them a byte long or empty. Reading such a partition
    // is not an error: the bytes that are not there are zeroes.
    var reader = new Vp8BoolDecoder(new byte[] { 0x42 }, 0, length);

    Assert.That(() => {
      for (var i = 0; i < 100; ++i)
        reader.ReadBool(128);
    }, Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void ReadingPastTheEndOfAPartitionTakesZeroes() {
    // The encoder flushes four bytes and a thrifty frame can leave the decoder wanting more than it
    // wrote. What matters is that it keeps answering rather than running off the packet.
    var stream = new Vp8TestStream();
    stream.Literal(8, 0xA5);
    var bytes = stream.Finish();

    var reader = new Vp8BoolDecoder(bytes, 0, bytes.Length);
    Assert.That(reader.ReadLiteral(8), Is.EqualTo(0xA5));
    Assert.That(() => {
      for (var i = 0; i < 10000; ++i)
        reader.ReadBool(1);
    }, Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void APartitionIsReadFromWhereItStartsAndStopsWhereItEnds() {
    // Every partition after the first sits at an offset inside the packet, so the decoder has to be
    // given a window rather than a buffer — and must not read the partition that follows it.
    var first = new Vp8TestStream();
    first.Literal(8, 0x11);
    var firstBytes = first.Finish();

    var second = new Vp8TestStream();
    second.Literal(8, 0x22);
    var secondBytes = second.Finish();

    var packet = new byte[firstBytes.Length + secondBytes.Length];
    firstBytes.CopyTo(packet, 0);
    secondBytes.CopyTo(packet, firstBytes.Length);

    var reader = new Vp8BoolDecoder(packet, firstBytes.Length, secondBytes.Length);
    Assert.That(reader.ReadLiteral(8), Is.EqualTo(0x22));
  }
}
