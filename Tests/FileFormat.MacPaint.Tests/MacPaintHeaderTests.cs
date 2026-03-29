using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.MacPaint;

namespace FileFormat.MacPaint.Tests;

[TestFixture]
public sealed class MacPaintHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var patterns = new byte[304];
    for (var i = 0; i < patterns.Length; ++i)
      patterns[i] = (byte)(i * 5 % 256);

    var original = new MacPaintHeader(
      Version: 2,
      Patterns: patterns
    );
    var buffer = new byte[MacPaintHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = MacPaintHeader.ReadFrom(buffer);

    Assert.Multiple(() => {
      Assert.That(parsed.Version, Is.EqualTo(original.Version));
      Assert.That(parsed.Patterns, Is.EqualTo(original.Patterns));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[MacPaintHeader.StructSize];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), 2); // Version = 2
    data[4] = 0xAA;   // first byte of patterns
    data[307] = 0xBB;  // last byte of patterns

    var header = MacPaintHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Version, Is.EqualTo(2));
      Assert.That(header.Patterns[0], Is.EqualTo(0xAA));
      Assert.That(header.Patterns[303], Is.EqualTo(0xBB));
      Assert.That(header.Patterns.Length, Is.EqualTo(304));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var patterns = new byte[304];
    patterns[0] = 0x11;
    patterns[303] = 0x22;

    var header = new MacPaintHeader(
      Version: 3,
      Patterns: patterns
    );
    var buffer = new byte[MacPaintHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0)), Is.EqualTo(3));
      Assert.That(buffer[4], Is.EqualTo(0x11));
      Assert.That(buffer[307], Is.EqualTo(0x22));
      // Padding should be zero
      for (var i = 308; i < 512; ++i)
        Assert.That(buffer[i], Is.EqualTo(0), $"Padding byte at offset {i} should be 0");
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = MacPaintHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(MacPaintHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = MacPaintHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is512() {
    Assert.That(MacPaintHeader.StructSize, Is.EqualTo(512));
  }
}
