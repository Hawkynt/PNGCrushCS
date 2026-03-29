using System;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8LBitReaderTests {

  [Test]
  [Category("Unit")]
  public void Constructor_NullData_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new Vp8LBitReader(null!, 0));
  }

  [Test]
  [Category("Unit")]
  public void ReadBits_SingleBit_ReturnsLsb() {
    var data = new byte[] { 0b00000001 };
    var reader = new Vp8LBitReader(data, 0);

    Assert.That(reader.ReadBits(1), Is.EqualTo(1u));
  }

  [Test]
  [Category("Unit")]
  public void ReadBits_MultipleBits_ReadsLsbFirst() {
    var data = new byte[] { 0b10110100 };
    var reader = new Vp8LBitReader(data, 0);

    var first4 = reader.ReadBits(4);
    var next4 = reader.ReadBits(4);

    Assert.That(first4, Is.EqualTo(0b0100u));
    Assert.That(next4, Is.EqualTo(0b1011u));
  }

  [Test]
  [Category("Unit")]
  public void ReadBits_FullByte_ReturnsEntireByte() {
    var data = new byte[] { 0xAB };
    var reader = new Vp8LBitReader(data, 0);

    Assert.That(reader.ReadBits(8), Is.EqualTo(0xABu));
  }

  [Test]
  [Category("Unit")]
  public void ReadBits_CrossesByteBoundary() {
    var data = new byte[] { 0xFF, 0x00 };
    var reader = new Vp8LBitReader(data, 0);

    reader.ReadBits(4);
    var crossBoundary = reader.ReadBits(8);

    Assert.That(crossBoundary, Is.EqualTo(0x0Fu));
  }

  [Test]
  [Category("Unit")]
  public void ReadBits_16Bits_SpansMultipleBytes() {
    var data = new byte[] { 0x34, 0x12 };
    var reader = new Vp8LBitReader(data, 0);

    Assert.That(reader.ReadBits(16), Is.EqualTo(0x1234u));
  }

  [Test]
  [Category("Unit")]
  public void ReadBits_32Bits_ReadsFullWord() {
    var data = new byte[] { 0x78, 0x56, 0x34, 0x12 };
    var reader = new Vp8LBitReader(data, 0);

    Assert.That(reader.ReadBits(32), Is.EqualTo(0x12345678u));
  }

  [Test]
  [Category("Unit")]
  public void ReadBits_WithOffset_SkipsInitialBytes() {
    var data = new byte[] { 0xAA, 0xBB, 0xCC };
    var reader = new Vp8LBitReader(data, 1);

    Assert.That(reader.ReadBits(8), Is.EqualTo(0xBBu));
  }

  [Test]
  [Category("Unit")]
  public void PeekBits_DoesNotConsumeBits() {
    var data = new byte[] { 0x5A };
    var reader = new Vp8LBitReader(data, 0);

    var peeked = reader.PeekBits(4);
    var read = reader.ReadBits(4);

    Assert.That(peeked, Is.EqualTo(read));
  }

  [Test]
  [Category("Unit")]
  public void PeekBits_MultiplePeeks_ReturnSameValue() {
    var data = new byte[] { 0xDE };
    var reader = new Vp8LBitReader(data, 0);

    var peek1 = reader.PeekBits(8);
    var peek2 = reader.PeekBits(8);

    Assert.That(peek1, Is.EqualTo(peek2));
  }

  [Test]
  [Category("Unit")]
  public void SkipBits_AdvancesPosition() {
    var data = new byte[] { 0xFF, 0xAB };
    var reader = new Vp8LBitReader(data, 0);

    reader.SkipBits(8);
    var result = reader.ReadBits(8);

    Assert.That(result, Is.EqualTo(0xABu));
  }

  [Test]
  [Category("Unit")]
  public void SkipBits_NonAligned_SkipsCorrectCount() {
    var data = new byte[] { 0b11110101 };
    var reader = new Vp8LBitReader(data, 0);

    reader.SkipBits(3);
    var result = reader.ReadBits(5);

    Assert.That(result, Is.EqualTo(0b11110u));
  }

  [Test]
  [Category("Unit")]
  public void IsAtEnd_EmptyData_ReturnsTrue() {
    var data = Array.Empty<byte>();
    var reader = new Vp8LBitReader(data, 0);

    Assert.That(reader.IsAtEnd, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void IsAtEnd_AfterReadingAllBits_ReturnsTrue() {
    var data = new byte[] { 0x42 };
    var reader = new Vp8LBitReader(data, 0);

    reader.ReadBits(8);

    Assert.That(reader.IsAtEnd, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void IsAtEnd_WithRemainingBits_ReturnsFalse() {
    var data = new byte[] { 0x42 };
    var reader = new Vp8LBitReader(data, 0);

    reader.ReadBits(4);

    Assert.That(reader.IsAtEnd, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ReadBits_SequentialSmallReads_MatchSingleLargeRead() {
    var data1 = new byte[] { 0x78, 0x56, 0x34, 0x12 };
    var data2 = new byte[] { 0x78, 0x56, 0x34, 0x12 };
    var reader1 = new Vp8LBitReader(data1, 0);
    var reader2 = new Vp8LBitReader(data2, 0);

    var a = reader1.ReadBits(32);
    var b = reader2.ReadBits(8)
            | (reader2.ReadBits(8) << 8)
            | (reader2.ReadBits(8) << 16)
            | (reader2.ReadBits(8) << 24);

    Assert.That(a, Is.EqualTo(b));
  }

  [Test]
  [Category("Unit")]
  public void BufferRefill_LargeData_HandlesCorrectly() {
    var data = new byte[16];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)i;

    var reader = new Vp8LBitReader(data, 0);

    for (var i = 0; i < 16; ++i)
      Assert.That(reader.ReadBits(8), Is.EqualTo((uint)i));
  }

  [Test]
  [Category("Unit")]
  public void ReadBits_OddBitCounts_ProducesCorrectValues() {
    var data = new byte[] { 0b01011010, 0b11001100 };
    var reader = new Vp8LBitReader(data, 0);

    var bits3 = reader.ReadBits(3);
    var bits5 = reader.ReadBits(5);
    var bits7 = reader.ReadBits(7);

    Assert.That(bits3, Is.EqualTo(0b010u));
    Assert.That(bits5, Is.EqualTo(0b01011u));
    Assert.That(bits7, Is.EqualTo(0b1001100u));
  }

  [Test]
  [Category("Unit")]
  public void PeekBits_SingleBit_ReturnsLsb() {
    var data = new byte[] { 0b11111110 };
    var reader = new Vp8LBitReader(data, 0);

    Assert.That(reader.PeekBits(1), Is.EqualTo(0u));
  }

  [Test]
  [Category("EdgeCase")]
  public void ReadBits_OffsetAtEnd_IsAtEnd() {
    var data = new byte[] { 0x42 };
    var reader = new Vp8LBitReader(data, 1);

    Assert.That(reader.IsAtEnd, Is.True);
  }
}
