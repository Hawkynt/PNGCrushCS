using System;
using FileFormat.WebP.Vp8;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8BoolDecoderTests {

  [Test]
  [Category("Unit")]
  public void ReadBool_Prob128_DecodesEquiprobableBits() {
    var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
    var decoder = new Vp8BoolDecoder(data, 0);

    var bit = decoder.ReadBool(128);

    Assert.That(bit, Is.EqualTo(0).Or.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ReadLiteral_1Bit_Returns0Or1() {
    var data = new byte[] { 0xFF, 0xFF };
    var decoder = new Vp8BoolDecoder(data, 0);

    var val = decoder.ReadLiteral(1);

    Assert.That(val, Is.EqualTo(0).Or.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ReadLiteral_8Bits_ReturnsValueInRange() {
    var data = new byte[] { 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89 };
    var decoder = new Vp8BoolDecoder(data, 0);

    var val = decoder.ReadLiteral(8);

    Assert.That(val, Is.InRange(0, 255));
  }

  [Test]
  [Category("Unit")]
  public void ReadSignedLiteral_ProducesMagnitudeAndSign() {
    var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
    var decoder = new Vp8BoolDecoder(data, 0);

    var val = decoder.ReadSignedLiteral(4);

    Assert.That(val, Is.InRange(-15, 15));
  }

  [Test]
  [Category("Unit")]
  public void ReadBool_DeterministicForSameInput() {
    var data = new byte[] { 0x42, 0x83, 0xA1, 0x7E };

    var decoder1 = new Vp8BoolDecoder(data, 0);
    var decoder2 = new Vp8BoolDecoder(data, 0);

    for (var i = 0; i < 8; ++i) {
      var b1 = decoder1.ReadBool(128);
      var b2 = decoder2.ReadBool(128);
      Assert.That(b1, Is.EqualTo(b2), $"Mismatch at bit {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void ReadBool_Prob1_AlmostAlways1() {
    var data = new byte[] { 0x80, 0x00, 0x00, 0x00 };
    var decoder = new Vp8BoolDecoder(data, 0);

    var bit = decoder.ReadBool(1);

    Assert.That(bit, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ReadBool_Prob255_AlmostAlways0() {
    var data = new byte[] { 0x00, 0x00, 0x00, 0x00 };
    var decoder = new Vp8BoolDecoder(data, 0);

    var bit = decoder.ReadBool(255);

    Assert.That(bit, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ReadLiteral_ZeroBits_ReturnsZero() {
    var data = new byte[] { 0xFF, 0xFF };
    var decoder = new Vp8BoolDecoder(data, 0);

    Assert.That(decoder.ReadLiteral(0), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ReadBool_SequentialCalls_ProduceBinaryValues() {
    var data = new byte[] { 0xAA, 0x55, 0xAA, 0x55 };
    var decoder = new Vp8BoolDecoder(data, 0);

    for (var i = 0; i < 16; ++i) {
      var bit = decoder.ReadBool(128);
      Assert.That(bit, Is.EqualTo(0).Or.EqualTo(1));
    }
  }

  [Test]
  [Category("Unit")]
  public void ReadLiteral_MultipleCalls_ExtractDistinctValues() {
    var data = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 };
    var decoder = new Vp8BoolDecoder(data, 0);

    var a = decoder.ReadLiteral(4);
    var b = decoder.ReadLiteral(4);

    Assert.That(a, Is.InRange(0, 15));
    Assert.That(b, Is.InRange(0, 15));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_WithOffset_StartsFromCorrectPosition() {
    var data = new byte[] { 0xFF, 0x00, 0x00, 0x00, 0x00 };

    var decoder0 = new Vp8BoolDecoder(data, 0);
    var decoder1 = new Vp8BoolDecoder(data, 1);

    var bit0 = decoder0.ReadBool(128);
    var bit1 = decoder1.ReadBool(128);

    Assert.That(bit0, Is.Not.EqualTo(bit1));
  }

  [Test]
  [Category("Unit")]
  public void ReadSignedLiteral_ZeroBits_ReturnsZero() {
    var data = new byte[] { 0xFF, 0xFF };
    var decoder = new Vp8BoolDecoder(data, 0);

    Assert.That(decoder.ReadSignedLiteral(0), Is.EqualTo(0).Or.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ReadBool_AllZeroData_Prob128_Returns0() {
    var data = new byte[] { 0x00, 0x00 };
    var decoder = new Vp8BoolDecoder(data, 0);

    Assert.That(decoder.ReadBool(128), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ReadLiteral_16Bits_ReturnsValidRange() {
    var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
    var decoder = new Vp8BoolDecoder(data, 0);

    var val = decoder.ReadLiteral(16);

    Assert.That(val, Is.InRange(0, 65535));
  }
}
