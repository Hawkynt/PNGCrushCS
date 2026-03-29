using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class Vp8LHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new Vp8LHeader(0x2F, 0xDEADBEEF);
    Span<byte> buffer = stackalloc byte[Vp8LHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = Vp8LHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[5];
    data[0] = 0x2F;
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(1), 0x12345678);

    var header = Vp8LHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Signature, Is.EqualTo(0x2F));
      Assert.That(header.BitField, Is.EqualTo(0x12345678u));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new Vp8LHeader(0x2F, 0xAABBCCDD);
    var buffer = new byte[Vp8LHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo(0x2F));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(1)), Is.EqualTo(0xAABBCCDDu));
    });
  }

  [Test]
  public void Width_Height_HasAlpha_ComputedCorrectly() {
    // Width = 320 => (320-1) = 319 = 0x013F in low 14 bits
    // Height = 240 => (240-1) = 239 = 0x00EF in bits 14..27
    // HasAlpha = true => bit 28 = 1
    var widthMinus1 = 319u;
    var heightMinus1 = 239u;
    var alphaBit = 1u << 28;
    var bitField = widthMinus1 | (heightMinus1 << 14) | alphaBit;

    var header = new Vp8LHeader(0x2F, bitField);
    Assert.Multiple(() => {
      Assert.That(header.Width, Is.EqualTo(320));
      Assert.That(header.Height, Is.EqualTo(240));
      Assert.That(header.HasAlpha, Is.True);
    });
  }

  [Test]
  public void HasAlpha_False_WhenBit28Clear() {
    var bitField = 99u | (49u << 14);
    var header = new Vp8LHeader(0x2F, bitField);
    Assert.That(header.HasAlpha, Is.False);
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = Vp8LHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(Vp8LHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = Vp8LHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is5() {
    Assert.That(Vp8LHeader.StructSize, Is.EqualTo(5));
  }
}
