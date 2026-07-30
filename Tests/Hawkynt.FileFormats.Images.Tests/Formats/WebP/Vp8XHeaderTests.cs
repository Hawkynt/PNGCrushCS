using System;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8XHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new Vp8XHeader(0x12, 0, 0, 0, 0xFF, 0x03, 0x00, 0xEF, 0x00, 0x00);
    Span<byte> buffer = stackalloc byte[Vp8XHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = Vp8XHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[10];
    data[0] = 0x10;
    data[1] = 0x00;
    data[2] = 0x00;
    data[3] = 0x00;
    data[4] = 0x3F;
    data[5] = 0x01;
    data[6] = 0x00;
    data[7] = 0xEF;
    data[8] = 0x00;
    data[9] = 0x00;

    var header = Vp8XHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Flags, Is.EqualTo(0x10));
      Assert.That(header.Reserved1, Is.EqualTo(0));
      Assert.That(header.CanvasWidthByte0, Is.EqualTo(0x3F));
      Assert.That(header.CanvasWidthByte1, Is.EqualTo(0x01));
      Assert.That(header.CanvasWidthByte2, Is.EqualTo(0x00));
      Assert.That(header.CanvasHeightByte0, Is.EqualTo(0xEF));
      Assert.That(header.CanvasHeightByte1, Is.EqualTo(0x00));
      Assert.That(header.CanvasHeightByte2, Is.EqualTo(0x00));
    });
  }

  [Test]
  public void CanvasWidth_CanvasHeight_ComputedCorrectly() {
    // Width = 1024 => 1024-1 = 1023 = 0x03FF => bytes: 0xFF, 0x03, 0x00
    // Height = 768 => 768-1 = 767 = 0x02FF => bytes: 0xFF, 0x02, 0x00
    var header = new Vp8XHeader(0x00, 0, 0, 0, 0xFF, 0x03, 0x00, 0xFF, 0x02, 0x00);
    Assert.Multiple(() => {
      Assert.That(header.CanvasWidth, Is.EqualTo(1024));
      Assert.That(header.CanvasHeight, Is.EqualTo(768));
    });
  }

  [Test]
  public void HasAlpha_True_WhenFlagBit4Set() {
    var header = new Vp8XHeader(0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    Assert.That(header.HasAlpha, Is.True);
  }

  [Test]
  public void HasAlpha_False_WhenFlagBit4Clear() {
    var header = new Vp8XHeader(0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    Assert.That(header.HasAlpha, Is.False);
  }

  [Test]
  public void IsAnimated_True_WhenFlagBit1Set() {
    var header = new Vp8XHeader(0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    Assert.That(header.IsAnimated, Is.True);
  }

  [Test]
  public void IsAnimated_False_WhenFlagBit1Clear() {
    var header = new Vp8XHeader(0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    Assert.That(header.IsAnimated, Is.False);
  }

  [Test]
  public void HasAlpha_IsAnimated_FromFlags() {
    var header = new Vp8XHeader(0x12, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    Assert.Multiple(() => {
      Assert.That(header.HasAlpha, Is.True);
      Assert.That(header.IsAnimated, Is.True);
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = Vp8XHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(Vp8XHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = Vp8XHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is10() {
    Assert.That(Vp8XHeader.StructSize, Is.EqualTo(10));
  }
}
