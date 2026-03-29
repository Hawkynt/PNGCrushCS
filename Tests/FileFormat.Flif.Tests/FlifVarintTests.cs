using System;
using FileFormat.Flif;

namespace FileFormat.Flif.Tests;

[TestFixture]
public sealed class FlifVarintTests {

  [Test]
  [Category("Unit")]
  public void Encode_Zero_SingleByte() {
    var buffer = new byte[8];
    var offset = 0;
    FlifVarint.Encode(buffer, ref offset, 0);

    Assert.That(offset, Is.EqualTo(1));
    Assert.That(buffer[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Encode_SmallValue_SingleByte() {
    var buffer = new byte[8];
    var offset = 0;
    FlifVarint.Encode(buffer, ref offset, 42);

    Assert.That(offset, Is.EqualTo(1));
    Assert.That(buffer[0], Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void Encode_127_SingleByte() {
    var buffer = new byte[8];
    var offset = 0;
    FlifVarint.Encode(buffer, ref offset, 127);

    Assert.That(offset, Is.EqualTo(1));
    Assert.That(buffer[0], Is.EqualTo(127));
  }

  [Test]
  [Category("Unit")]
  public void Encode_128_TwoBytes() {
    var buffer = new byte[8];
    var offset = 0;
    FlifVarint.Encode(buffer, ref offset, 128);

    Assert.That(offset, Is.EqualTo(2));
    Assert.That(buffer[0], Is.EqualTo(0x80)); // 128 & 0x7F = 0, with continuation bit
    Assert.That(buffer[1], Is.EqualTo(1));     // 128 >> 7 = 1
  }

  [Test]
  [Category("Unit")]
  public void Encode_LargeValue_MultipleBytes() {
    var buffer = new byte[8];
    var offset = 0;
    FlifVarint.Encode(buffer, ref offset, 16384);

    Assert.That(offset, Is.GreaterThan(2));
  }

  [Test]
  [Category("Unit")]
  public void Decode_Zero_ReturnsZero() {
    var data = new byte[] { 0 };
    var offset = 0;
    var result = FlifVarint.Decode(data.AsSpan(), ref offset);

    Assert.That(result, Is.EqualTo(0));
    Assert.That(offset, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Decode_SmallValue_ReturnsSingleByte() {
    var data = new byte[] { 42 };
    var offset = 0;
    var result = FlifVarint.Decode(data.AsSpan(), ref offset);

    Assert.That(result, Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SmallValue() {
    var buffer = new byte[8];
    var offset = 0;
    FlifVarint.Encode(buffer, ref offset, 99);

    var readOffset = 0;
    var decoded = FlifVarint.Decode(buffer.AsSpan(), ref readOffset);

    Assert.That(decoded, Is.EqualTo(99));
    Assert.That(readOffset, Is.EqualTo(offset));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_128() {
    var buffer = new byte[8];
    var offset = 0;
    FlifVarint.Encode(buffer, ref offset, 128);

    var readOffset = 0;
    var decoded = FlifVarint.Decode(buffer.AsSpan(), ref readOffset);

    Assert.That(decoded, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeValue() {
    var buffer = new byte[8];
    var offset = 0;
    FlifVarint.Encode(buffer, ref offset, 100000);

    var readOffset = 0;
    var decoded = FlifVarint.Decode(buffer.AsSpan(), ref readOffset);

    Assert.That(decoded, Is.EqualTo(100000));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MaxWidth() {
    var buffer = new byte[8];
    var offset = 0;
    var value = 65535; // max dimension - 1
    FlifVarint.Encode(buffer, ref offset, value);

    var readOffset = 0;
    var decoded = FlifVarint.Decode(buffer.AsSpan(), ref readOffset);

    Assert.That(decoded, Is.EqualTo(value));
  }

  [Test]
  [Category("Unit")]
  public void EncodedLength_Zero_IsOne() {
    Assert.That(FlifVarint.EncodedLength(0), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void EncodedLength_127_IsOne() {
    Assert.That(FlifVarint.EncodedLength(127), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void EncodedLength_128_IsTwo() {
    Assert.That(FlifVarint.EncodedLength(128), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void EncodedLength_16384_IsThree() {
    Assert.That(FlifVarint.EncodedLength(16384), Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Encode_NegativeValue_ThrowsArgumentOutOfRangeException() {
    var buffer = new byte[8];
    var offset = 0;
    Assert.Throws<ArgumentOutOfRangeException>(() => FlifVarint.Encode(buffer, ref offset, -1));
  }

  [Test]
  [Category("Unit")]
  public void EncodedLength_NegativeValue_ThrowsArgumentOutOfRangeException() {
    Assert.Throws<ArgumentOutOfRangeException>(() => FlifVarint.EncodedLength(-1));
  }
}
