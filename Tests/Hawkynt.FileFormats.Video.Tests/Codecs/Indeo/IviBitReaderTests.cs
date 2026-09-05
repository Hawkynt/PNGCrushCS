using System;

namespace FileFormat.Codecs.Indeo.Tests;

/// <summary>
/// The bit reader, whose only job is to take bits in the order Indeo wrote them.
/// </summary>
/// <remarks>
/// Indeo is little-endian in its bits as well as its bytes, which most formats of its era are not.
/// Reading it the other way round yields field values that are mostly still in range — a frame type,
/// a band flag, a block size — so the mistake shows up as a picture made of noise rather than as an
/// error, which is why the order gets a test of its own.
/// </remarks>
[TestFixture]
public sealed class IviBitReaderTests {

  [Test]
  [Category("Unit")]
  public void BitsComeOutLeastSignificantFirst() {
    var reader = new IviBitReader(new byte[] { 0b1011_0010 });
    foreach (var expected in new[] { 0, 1, 0, 0, 1, 1, 0, 1 })
      Assert.That(reader.ReadBit(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void AFieldTakesItsFirstBitAsItsLowestOne() {
    // 0b101 read as three bits is 5 and not 1: the first bit read is the field's lowest.
    var reader = new IviBitReader(new byte[] { 0b0000_0101 });
    Assert.That(reader.Read(3), Is.EqualTo(5u));
  }

  [Test]
  [Category("Unit")]
  public void AFieldCrossingAByteBoundaryContinuesIntoTheLowBitsOfTheNext() {
    // The second byte's low three bits become the field's high three.
    var reader = new IviBitReader(new byte[] { 0b1100_0000, 0b0000_0101 });
    reader.Skip(6);
    Assert.That(reader.Read(6), Is.EqualTo(0b101_11u));
    Assert.That(reader.Position, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void AThirtyTwoBitFieldComesOutWhole() {
    var reader = new IviBitReader(new byte[] { 0x78, 0x56, 0x34, 0x12 });
    Assert.That(reader.Read(32), Is.EqualTo(0x12345678u));
  }

  [Test]
  [Category("Unit")]
  public void AligningMovesToTheNextByteBoundaryAndNoFurther() {
    var reader = new IviBitReader(new byte[] { 0xFF, 0xFF });
    reader.Skip(9);
    reader.Align();
    Assert.That(reader.Position, Is.EqualTo(16));

    reader.Align();
    Assert.That(reader.Position, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ReadingPastTheEndYieldsZeroesAndLeavesABitCountThatSaysSo() {
    // Zeroes rather than an exception on purpose: an Indeo frame states its own tile and band sizes
    // and the decoder checks the position against them afterwards, so the overshoot has to come back
    // for the check that catches it to run at all.
    var reader = new IviBitReader(new byte[] { 0xFF });
    Assert.That(reader.Read(8), Is.EqualTo(0xFFu));
    Assert.That(reader.Read(8), Is.Zero);
    Assert.That(reader.BitsLeft, Is.EqualTo(-8));
  }

  [Test]
  [Category("Unit")]
  public void PeekingDoesNotAdvance() {
    var reader = new IviBitReader(new byte[] { 0xAB });
    Assert.That(reader.Peek(8), Is.EqualTo(0xABu));
    Assert.That(reader.Position, Is.Zero);
    Assert.That(reader.Read(8), Is.EqualTo(0xABu));
  }

  [Test]
  [Category("Unit")]
  public void WhatIsLeftIsTheWholeBytesFromWhereTheReaderStands() {
    var reader = new IviBitReader(new byte[] { 1, 2, 3, 4 });
    reader.Skip(16);
    Assert.That(reader.Remaining.ToArray(), Is.EqualTo(new byte[] { 3, 4 }));

    reader.Skip(16);
    Assert.That(reader.Remaining.Length, Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyPacketReadsAsZeroes() {
    var reader = new IviBitReader(ReadOnlyMemory<byte>.Empty);
    Assert.That(reader.Length, Is.Zero);
    Assert.That(reader.Read(5), Is.Zero);
  }
}
