using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Qoi;
using FileFormat.Core;

namespace FileFormat.Qoi.Tests;

[TestFixture]
public sealed class QoiHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new QoiHeader((byte)'q', (byte)'o', (byte)'i', (byte)'f', 640, 480, QoiChannels.Rgba, QoiColorSpace.Linear);
    Span<byte> buffer = stackalloc byte[QoiHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = QoiHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[14];
    data[0] = (byte)'q';
    data[1] = (byte)'o';
    data[2] = (byte)'i';
    data[3] = (byte)'f';
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 1024);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 768);
    data[12] = 3; // RGB
    data[13] = 0; // sRGB

    var header = QoiHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Magic1, Is.EqualTo((byte)'q'));
      Assert.That(header.Magic2, Is.EqualTo((byte)'o'));
      Assert.That(header.Magic3, Is.EqualTo((byte)'i'));
      Assert.That(header.Magic4, Is.EqualTo((byte)'f'));
      Assert.That(header.Width, Is.EqualTo(1024u));
      Assert.That(header.Height, Is.EqualTo(768u));
      Assert.That(header.Channels, Is.EqualTo(QoiChannels.Rgb));
      Assert.That(header.ColorSpace, Is.EqualTo(QoiColorSpace.Srgb));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new QoiHeader((byte)'q', (byte)'o', (byte)'i', (byte)'f', 320, 240, QoiChannels.Rgba, QoiColorSpace.Linear);
    var buffer = new byte[QoiHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo((byte)'q'));
      Assert.That(buffer[1], Is.EqualTo((byte)'o'));
      Assert.That(buffer[2], Is.EqualTo((byte)'i'));
      Assert.That(buffer[3], Is.EqualTo((byte)'f'));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4)), Is.EqualTo(320u));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(8)), Is.EqualTo(240u));
      Assert.That(buffer[12], Is.EqualTo(4));
      Assert.That(buffer[13], Is.EqualTo(1));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = QoiHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(QoiHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = QoiHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is14() {
    Assert.That(QoiHeader.StructSize, Is.EqualTo(14));
  }
}
