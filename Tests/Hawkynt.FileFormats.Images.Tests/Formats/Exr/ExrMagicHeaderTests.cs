using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Exr;
using FileFormat.Core;

namespace FileFormat.Exr.Tests;

[TestFixture]
public sealed class ExrMagicHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new ExrMagicHeader(ExrMagicHeader.ExpectedMagic, ExrMagicHeader.ExpectedVersion);
    Span<byte> buffer = stackalloc byte[ExrMagicHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = ExrMagicHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[8];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), ExrMagicHeader.ExpectedMagic);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), ExrMagicHeader.ExpectedVersion);

    var header = ExrMagicHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Magic, Is.EqualTo(ExrMagicHeader.ExpectedMagic));
      Assert.That(header.Version, Is.EqualTo(ExrMagicHeader.ExpectedVersion));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new ExrMagicHeader(ExrMagicHeader.ExpectedMagic, ExrMagicHeader.ExpectedVersion);
    var buffer = new byte[ExrMagicHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo(0x76));
      Assert.That(buffer[1], Is.EqualTo(0x2F));
      Assert.That(buffer[2], Is.EqualTo(0x31));
      Assert.That(buffer[3], Is.EqualTo(0x01));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4)), Is.EqualTo(ExrMagicHeader.ExpectedVersion));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = ExrMagicHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(ExrMagicHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = ExrMagicHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is8() {
    Assert.That(ExrMagicHeader.StructSize, Is.EqualTo(8));
  }
}
