using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bpg;

namespace FileFormat.Bpg.Tests;

[TestFixture]
public sealed class BpgUe7Tests {

  [Test]
  [Category("Unit")]
  public void Encode_Zero_SingleByte() {
    var output = new List<byte>();
    BpgUe7.Write(output, 0);
    Assert.That(output, Is.EqualTo(new byte[] { 0 }));
  }

  [TestCase(100)]
  [TestCase(127)]
  [Category("Unit")]
  public void Encode_SmallValue_SingleByte(int value) {
    var output = new List<byte>();
    BpgUe7.Write(output, value);
    Assert.That(output, Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void Encode_128_TwoBytes() {
    var output = new List<byte>();
    BpgUe7.Write(output, 128);
    Assert.That(output, Is.EqualTo(new byte[] { 0x81, 0x00 }));
  }

  [Test]
  [Category("Unit")]
  public void Encode_NegativeValue_Throws() {
    var output = new List<byte>();
    Assert.Throws<ArgumentOutOfRangeException>(() => BpgUe7.Write(output, -1));
  }

  [TestCase(new byte[] { 0x00 }, 0)]
  [TestCase(new byte[] { 0x2a }, 42)]
  [TestCase(new byte[] { 0x81, 0x00 }, 128)]
  [TestCase(new byte[] { 0x84, 0x1e }, 542)]
  [Category("Unit")]
  public void Decode_CanonicalValue_ReturnsValue(byte[] data, int expected) {
    var offset = 0;
    var result = BpgUe7.Read(data, ref offset);
    Assert.Multiple(() => {
      Assert.That(result, Is.EqualTo(expected));
      Assert.That(offset, Is.EqualTo(data.Length));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_EmptyData_ThrowsInvalidData() {
    var data = Array.Empty<byte>();
    var offset = 0;
    Assert.Throws<InvalidDataException>(() => BpgUe7.Read(data, ref offset));
  }

  [TestCase(new byte[] { 0x80, 0x00 }, TestName = "Decode_LeadingZeroGroup_IsRejected")]
  [TestCase(new byte[] { 0x90, 0x80, 0x80, 0x80, 0x00 }, TestName = "Decode_MoreThan32Bits_IsRejected")]
  [TestCase(new byte[] { 0x81, 0x80, 0x80, 0x80, 0x80 }, TestName = "Decode_UnterminatedFiveByteValue_IsRejected")]
  [Category("Unit")]
  public void Decode_NonCanonicalOrOverflowingValue_Throws(byte[] data) {
    var offset = 0;
    Assert.Throws<InvalidDataException>(() => BpgUe7.ReadUInt32(data, ref offset));
  }

  [Test]
  [Category("Unit")]
  public void Decode_UInt32Maximum_IsAcceptedByUnsignedReader() {
    byte[] data = [0x8f, 0xff, 0xff, 0xff, 0x7f];
    var offset = 0;
    Assert.That(BpgUe7.ReadUInt32(data, ref offset), Is.EqualTo(uint.MaxValue));
  }

  [Test]
  [Category("Unit")]
  public void Decode_ValuePastSignedModel_IsRejectedBySignedReader() {
    byte[] data = [0x88, 0x80, 0x80, 0x80, 0x00]; // 2^31
    var offset = 0;
    Assert.Throws<InvalidDataException>(() => BpgUe7.Read(data, ref offset));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_RepresentativeSignedValues() {
    int[] values = [0, 1, 127, 128, 255, 256, 1000, 16383, 16384, 65535, 100000, int.MaxValue];
    foreach (var value in values) {
      var output = new List<byte>();
      BpgUe7.Write(output, value);
      var offset = 0;
      Assert.That(BpgUe7.Read([.. output], ref offset), Is.EqualTo(value), $"Round-trip failed for {value}");
      Assert.That(offset, Is.EqualTo(output.Count));
    }
  }

  [Test]
  [Category("Unit")]
  public void Decode_WithOffset_AdvancesCorrectly() {
    var output = new List<byte>();
    BpgUe7.Write(output, 5);
    BpgUe7.Write(output, 200);
    var data = output.ToArray();
    var offset = 0;

    Assert.Multiple(() => {
      Assert.That(BpgUe7.Read(data, ref offset), Is.EqualTo(5));
      Assert.That(BpgUe7.Read(data, ref offset), Is.EqualTo(200));
    });
  }
}
