using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.CrackArt;

namespace FileFormat.CrackArt.Tests;

[TestFixture]
public sealed class CrackArtHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var original = new CrackArtHeader(2, palette);
    Span<byte> buffer = stackalloc byte[CrackArtHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = CrackArtHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[CrackArtHeader.StructSize];
    data[0] = 1; // Resolution = Medium
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(1), 0x777); // Palette[0] = white
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(3), 0x700); // Palette[1] = red
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(5), 0x070); // Palette[2] = green
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(7), 0x007); // Palette[3] = blue

    var header = CrackArtHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Resolution, Is.EqualTo(1));
      Assert.That(header.Palette[0], Is.EqualTo(0x777));
      Assert.That(header.Palette[1], Is.EqualTo(0x700));
      Assert.That(header.Palette[2], Is.EqualTo(0x070));
      Assert.That(header.Palette[3], Is.EqualTo(0x007));
    });
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is33() {
    Assert.That(CrackArtHeader.StructSize, Is.EqualTo(33));
  }
}
