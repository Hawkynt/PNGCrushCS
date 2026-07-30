using System;
using System.Collections.Generic;
using FileFormat.Bpg;

namespace FileFormat.Bpg.Tests;

[TestFixture]
public sealed class BpgUe7Tests {

  [Test]
  [Category("Unit")]
  public void Encode_Zero_SingleByte() {
    var output = new List<byte>();
    BpgUe7.Write(output, 0);

    Assert.That(output, Has.Count.EqualTo(1));
    Assert.That(output[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Encode_SmallValue_SingleByte() {
    var output = new List<byte>();
    BpgUe7.Write(output, 100);

    Assert.That(output, Has.Count.EqualTo(1));
    Assert.That(output[0], Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void Encode_127_SingleByte() {
    var output = new List<byte>();
    BpgUe7.Write(output, 127);

    Assert.That(output, Has.Count.EqualTo(1));
    Assert.That(output[0], Is.EqualTo(127));
  }

  [Test]
  [Category("Unit")]
  public void Encode_128_TwoBytes() {
    var output = new List<byte>();
    BpgUe7.Write(output, 128);

    Assert.That(output, Has.Count.EqualTo(2));
    // 128 = 0b10_0000000 => two 7-bit groups: [1, 0]
    // First byte: 0x80 | 1 = 0x81
    // Second byte: 0
    Assert.That(output[0], Is.EqualTo(0x81));
    Assert.That(output[1], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void Encode_VeryLarge_ThreeBytes() {
    var output = new List<byte>();
    BpgUe7.Write(output, 16384);

    // 16384 = 0x4000 = 0b1_0000000_0000000 => three 7-bit groups
    Assert.That(output, Has.Count.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Encode_NegativeValue_Throws() {
    var output = new List<byte>();
    Assert.Throws<ArgumentOutOfRangeException>(() => BpgUe7.Write(output, -1));
  }

  [Test]
  [Category("Unit")]
  public void Decode_Zero_ReturnsZero() {
    var data = new byte[] { 0 };
    var offset = 0;
    var result = BpgUe7.Read(data, ref offset);

    Assert.That(result, Is.EqualTo(0));
    Assert.That(offset, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Decode_SmallValue_ReturnsSingleByte() {
    var data = new byte[] { 42 };
    var offset = 0;
    var result = BpgUe7.Read(data, ref offset);

    Assert.That(result, Is.EqualTo(42));
    Assert.That(offset, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Decode_TwoBytes_ReturnsLargeValue() {
    // Encode 128: first byte = 0x81 (more=1, value=1), second byte = 0x00 (more=0, value=0)
    var data = new byte[] { 0x81, 0x00 };
    var offset = 0;
    var result = BpgUe7.Read(data, ref offset);

    Assert.That(result, Is.EqualTo(128));
    Assert.That(offset, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void Decode_EmptyData_Throws() {
    var data = Array.Empty<byte>();
    var offset = 0;
    Assert.Throws<InvalidOperationException>(() => BpgUe7.Read(data, ref offset));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SmallValues() {
    for (var i = 0; i < 128; ++i) {
      var output = new List<byte>();
      BpgUe7.Write(output, i);

      var offset = 0;
      var result = BpgUe7.Read(output.ToArray(), ref offset);
      Assert.That(result, Is.EqualTo(i), $"Round-trip failed for value {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeValues() {
    int[] values = [128, 255, 256, 1000, 1920, 4096, 16383, 16384, 65535, 100000];
    foreach (var value in values) {
      var output = new List<byte>();
      BpgUe7.Write(output, value);

      var offset = 0;
      var result = BpgUe7.Read(output.ToArray(), ref offset);
      Assert.That(result, Is.EqualTo(value), $"Round-trip failed for value {value}");
    }
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_VeryLargeValue() {
    var output = new List<byte>();
    BpgUe7.Write(output, 0x7FFFFFFF);

    var offset = 0;
    var result = BpgUe7.Read(output.ToArray(), ref offset);
    Assert.That(result, Is.EqualTo(0x7FFFFFFF));
  }

  [Test]
  [Category("Unit")]
  public void Decode_WithOffset_AdvancesCorrectly() {
    // Two ue7 values back to back: 5 (single byte), then 200 (two bytes)
    var output = new List<byte>();
    BpgUe7.Write(output, 5);
    BpgUe7.Write(output, 200);

    var data = output.ToArray();
    var offset = 0;

    var first = BpgUe7.Read(data, ref offset);
    var second = BpgUe7.Read(data, ref offset);

    Assert.That(first, Is.EqualTo(5));
    Assert.That(second, Is.EqualTo(200));
  }
}
