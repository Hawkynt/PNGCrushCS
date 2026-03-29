using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.AmigaIcon;
using FileFormat.Core;

namespace FileFormat.AmigaIcon.Tests;

[TestFixture]
public sealed class AmigaIconHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is78() {
    Assert.That(AmigaIconHeader.StructSize, Is.EqualTo(78));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesMagic() {
    var data = new byte[78];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 0xE310);

    var header = AmigaIconHeader.ReadFrom(data);

    Assert.That(header.Magic, Is.EqualTo(0xE310));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesDimensions() {
    var data = new byte[78];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 0xE310);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(10), 32);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(12), 16);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(14), 2);

    var header = AmigaIconHeader.ReadFrom(data);

    Assert.That(header.Width, Is.EqualTo(32));
    Assert.That(header.Height, Is.EqualTo(16));
    Assert.That(header.Depth, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesIconType() {
    var data = new byte[78];
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0), 0xE310);
    data[54] = 5;

    var header = AmigaIconHeader.ReadFrom(data);

    Assert.That(header.IconTypeByte, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversAllMappedFields() {
    var map = AmigaIconHeader.GetFieldMap();
    Assert.That(map.Length, Is.GreaterThan(0));

    // Should have entries for the mapped fields and filler regions
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(AmigaIconHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_WriteThenRead_PreservesMappedFields() {
    var header = new AmigaIconHeader {
      Magic = 0xE310,
      Version = 1,
      Width = 48,
      Height = 24,
      Depth = 3,
      ImageDataPointer = 1,
      PlanePick = 7,
      PlaneOnOff = 0,
      IconTypeByte = 4,
    };

    var buffer = new byte[AmigaIconHeader.StructSize];
    header.WriteTo(buffer);
    var parsed = AmigaIconHeader.ReadFrom(buffer);

    Assert.That(parsed.Magic, Is.EqualTo(header.Magic));
    Assert.That(parsed.Version, Is.EqualTo(header.Version));
    Assert.That(parsed.Width, Is.EqualTo(header.Width));
    Assert.That(parsed.Height, Is.EqualTo(header.Height));
    Assert.That(parsed.Depth, Is.EqualTo(header.Depth));
    Assert.That(parsed.ImageDataPointer, Is.EqualTo(header.ImageDataPointer));
    Assert.That(parsed.PlanePick, Is.EqualTo(header.PlanePick));
    Assert.That(parsed.PlaneOnOff, Is.EqualTo(header.PlaneOnOff));
    Assert.That(parsed.IconTypeByte, Is.EqualTo(header.IconTypeByte));
  }
}
