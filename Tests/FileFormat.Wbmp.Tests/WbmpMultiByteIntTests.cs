using System;
using FileFormat.Wbmp;

namespace FileFormat.Wbmp.Tests;

[TestFixture]
public sealed class WbmpMultiByteIntTests {

  [Test]
  [Category("Unit")]
  public void Encode_Zero_ReturnsSingleZeroByte() {
    var result = WbmpMultiByteInt.Encode(0);

    Assert.That(result, Has.Length.EqualTo(1));
    Assert.That(result[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Encode_SmallValue_ReturnsSingleByte() {
    // Values 0-127 fit in a single byte
    var result = WbmpMultiByteInt.Encode(100);

    Assert.That(result, Has.Length.EqualTo(1));
    Assert.That(result[0], Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void Encode_127_ReturnsSingleByte() {
    var result = WbmpMultiByteInt.Encode(127);

    Assert.That(result, Has.Length.EqualTo(1));
    Assert.That(result[0], Is.EqualTo(127));
  }

  [Test]
  [Category("Unit")]
  public void Encode_128_ReturnsTwoBytes() {
    // 128 = 0b10000000 => 0x81 0x00
    var result = WbmpMultiByteInt.Encode(128);

    Assert.That(result, Has.Length.EqualTo(2));
    Assert.That(result[0], Is.EqualTo(0x81));
    Assert.That(result[1], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void Encode_200_ReturnsTwoBytes() {
    // 200 = 0b11001000 => 0x81 0x48
    var result = WbmpMultiByteInt.Encode(200);

    Assert.That(result, Has.Length.EqualTo(2));
    Assert.That(result[0], Is.EqualTo(0x81));
    Assert.That(result[1], Is.EqualTo(0x48));
  }

  [Test]
  [Category("Unit")]
  public void Encode_16384_ReturnsThreeBytes() {
    // 16384 = 0x4000 => 0x81 0x80 0x00
    var result = WbmpMultiByteInt.Encode(16384);

    Assert.That(result, Has.Length.EqualTo(3));
    Assert.That(result[0], Is.EqualTo(0x81));
    Assert.That(result[1], Is.EqualTo(0x80));
    Assert.That(result[2], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void Decode_SingleByte_ReturnsValue() {
    var data = new byte[] { 42 };
    var result = WbmpMultiByteInt.Decode(data, out var consumed);

    Assert.That(result, Is.EqualTo(42));
    Assert.That(consumed, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Decode_TwoBytes_ReturnsValue() {
    // 200 = 0x81 0x48
    var data = new byte[] { 0x81, 0x48 };
    var result = WbmpMultiByteInt.Decode(data, out var consumed);

    Assert.That(result, Is.EqualTo(200));
    Assert.That(consumed, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SmallValues() {
    for (var i = 0; i < 128; ++i) {
      var encoded = WbmpMultiByteInt.Encode(i);
      var decoded = WbmpMultiByteInt.Decode(encoded, out var consumed);

      Assert.That(decoded, Is.EqualTo(i), $"Failed round-trip for value {i}");
      Assert.That(consumed, Is.EqualTo(encoded.Length));
    }
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeValues() {
    int[] values = [128, 200, 255, 256, 1000, 16383, 16384, 32767, 65535];
    foreach (var value in values) {
      var encoded = WbmpMultiByteInt.Encode(value);
      var decoded = WbmpMultiByteInt.Decode(encoded, out var consumed);

      Assert.That(decoded, Is.EqualTo(value), $"Failed round-trip for value {value}");
      Assert.That(consumed, Is.EqualTo(encoded.Length));
    }
  }

  [Test]
  [Category("Unit")]
  public void Decode_WithTrailingData_ConsumesOnlyNeededBytes() {
    // Value 5 (single byte) followed by extra data
    var data = new byte[] { 5, 99, 100 };
    var result = WbmpMultiByteInt.Decode(data, out var consumed);

    Assert.That(result, Is.EqualTo(5));
    Assert.That(consumed, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Encode_NegativeValue_ThrowsArgumentOutOfRangeException() {
    Assert.Throws<ArgumentOutOfRangeException>(() => WbmpMultiByteInt.Encode(-1));
  }
}
