using System.IO;

namespace FileFormat.Codecs.Vp3.Tests;

/// <summary>
/// The bit reader, whose only job is to take bits in the order VP3 wrote them.
/// </summary>
/// <remarks>
/// Most bit readers in this library take the least significant bit of a byte first; this one takes
/// the most significant, which is Section 5.2 of the Theora specification and the opposite of what
/// Vorbis does with the same container. Getting it backwards would not fail loudly — it would decode
/// a frame header into plausible nonsense — so the order is worth a test of its own.
/// </remarks>
[TestFixture]
public sealed class Vp3BitReaderTests {

  [Test]
  [Category("Unit")]
  public void BitsComeOutMostSignificantFirst() {
    var reader = new Vp3BitReader(new byte[] { 0b1011_0010 });
    foreach (var expected in new[] { 1, 0, 1, 1, 0, 0, 1, 0 })
      Assert.That(reader.ReadBit(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void AFieldWiderThanWhatIsLeftOfAByteContinuesIntoTheNext() {
    // Nothing in a VP3 frame is aligned to anything, so this is the usual case rather than an edge
    // one: the six-bit quantisation index of a frame header starts at bit two and ends at bit seven,
    // and the field after it starts in the next byte.
    var reader = new Vp3BitReader(new byte[] { 0b0000_0111, 0b1100_0000 });
    Assert.That(reader.ReadBits(5), Is.Zero);
    Assert.That(reader.ReadBits(6), Is.EqualTo(0b111_110));
    Assert.That(reader.Position, Is.EqualTo(11));
  }

  [Test]
  [Category("Unit")]
  public void ReadingNoBitsReadsNothing() {
    var reader = new Vp3BitReader(new byte[] { 0xFF });
    Assert.That(reader.ReadBits(0), Is.Zero);
    Assert.That(reader.Position, Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void ThePositionAndLengthAreCountedInBits() {
    var reader = new Vp3BitReader(new byte[] { 0x00, 0x00, 0x00 });
    Assert.That(reader.Length, Is.EqualTo(24));
    reader.ReadBits(9);
    Assert.That(reader.Position, Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void ReadingPastTheEndOfThePacketThrowsAndSaysHowLongItWas() {
    // Not zeroes: a VP3 frame's fields are positional, so the first read past the end means the
    // position was already wrong and everything after it would be noise that still made a picture.
    var reader = new Vp3BitReader(new byte[] { 0xFF, 0xFF });
    reader.ReadBits(16);

    var failure = Assert.Throws<InvalidDataException>(() => reader.ReadBit());
    Assert.That(failure!.Message, Does.Contain("2-byte packet"));
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyPacketHasNoBitsToRead() {
    var reader = new Vp3BitReader(System.Array.Empty<byte>());
    Assert.That(reader.Length, Is.Zero);
    Assert.Throws<InvalidDataException>(() => reader.ReadBit());
  }
}
