using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8FrameHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new Vp8FrameHeader(0x00, 0x01, 0x02, 0x9D, 0x01, 0x2A, 320, 240);
    Span<byte> buffer = stackalloc byte[Vp8FrameHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = Vp8FrameHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[10];
    data[0] = 0x00;
    data[1] = 0x10;
    data[2] = 0x20;
    data[3] = 0x9D;
    data[4] = 0x01;
    data[5] = 0x2A;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 640);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 480);

    var header = Vp8FrameHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.FrameTag0, Is.EqualTo(0x00));
      Assert.That(header.FrameTag1, Is.EqualTo(0x10));
      Assert.That(header.FrameTag2, Is.EqualTo(0x20));
      Assert.That(header.Signature0, Is.EqualTo(0x9D));
      Assert.That(header.Signature1, Is.EqualTo(0x01));
      Assert.That(header.Signature2, Is.EqualTo(0x2A));
      Assert.That(header.WidthAndScale, Is.EqualTo(640));
      Assert.That(header.HeightAndScale, Is.EqualTo(480));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new Vp8FrameHeader(0x00, 0x11, 0x22, 0x9D, 0x01, 0x2A, 1920, 1080);
    var buffer = new byte[Vp8FrameHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo(0x00));
      Assert.That(buffer[1], Is.EqualTo(0x11));
      Assert.That(buffer[2], Is.EqualTo(0x22));
      Assert.That(buffer[3], Is.EqualTo(0x9D));
      Assert.That(buffer[4], Is.EqualTo(0x01));
      Assert.That(buffer[5], Is.EqualTo(0x2A));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(6)), Is.EqualTo(1920));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(8)), Is.EqualTo(1080));
    });
  }

  [Test]
  public void IsKeyframe_True_WhenBit0Clear() {
    var header = new Vp8FrameHeader(0x00, 0, 0, 0x9D, 0x01, 0x2A, 0, 0);
    Assert.That(header.IsKeyframe, Is.True);
  }

  [Test]
  public void IsKeyframe_False_WhenBit0Set() {
    var header = new Vp8FrameHeader(0x01, 0, 0, 0x9D, 0x01, 0x2A, 0, 0);
    Assert.That(header.IsKeyframe, Is.False);
  }

  [Test]
  public void IsKeyframe_True_WhenOtherBitsSet() {
    var header = new Vp8FrameHeader(0xFE, 0, 0, 0x9D, 0x01, 0x2A, 0, 0);
    Assert.That(header.IsKeyframe, Is.True);
  }

  [Test]
  public void HasValidSignature_Checks9D012A() {
    var valid = new Vp8FrameHeader(0x00, 0, 0, 0x9D, 0x01, 0x2A, 0, 0);
    var invalid = new Vp8FrameHeader(0x00, 0, 0, 0x00, 0x00, 0x00, 0, 0);
    Assert.Multiple(() => {
      Assert.That(valid.HasValidSignature, Is.True);
      Assert.That(invalid.HasValidSignature, Is.False);
    });
  }

  [Test]
  public void Width_Height_MaskedCorrectly() {
    // Width and height use only the low 14 bits; upper 2 bits are scale
    var widthWithScale = (ushort)(800 | (2 << 14));
    var heightWithScale = (ushort)(600 | (1 << 14));
    var header = new Vp8FrameHeader(0x00, 0, 0, 0x9D, 0x01, 0x2A, widthWithScale, heightWithScale);
    Assert.Multiple(() => {
      Assert.That(header.Width, Is.EqualTo(800));
      Assert.That(header.Height, Is.EqualTo(600));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = Vp8FrameHeader.GetFieldMap();
    var maxEnd = map.Max(f => f.Offset + f.Size);
    Assert.That(maxEnd, Is.EqualTo(Vp8FrameHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_ContainsAllExpectedEntries() {
    var map = Vp8FrameHeader.GetFieldMap();
    Assert.That(map.Any(f => f.Name == "FrameTag" && f.Offset == 0 && f.Size == 3), Is.True);
    Assert.That(map.Any(f => f.Name == "Signature" && f.Offset == 3 && f.Size == 3), Is.True);
    Assert.That(map.Any(f => f.Name == "WidthAndScale" && f.Offset == 6 && f.Size == 2), Is.True);
    Assert.That(map.Any(f => f.Name == "HeightAndScale" && f.Offset == 8 && f.Size == 2), Is.True);
  }

  [Test]
  public void StructSize_Is10() {
    Assert.That(Vp8FrameHeader.StructSize, Is.EqualTo(10));
  }
}
