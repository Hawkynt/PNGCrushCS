using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Psd;

namespace FileFormat.Psd.Tests;

[TestFixture]
public sealed class PsdHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new PsdHeader(
      (byte)'8', (byte)'B', (byte)'P', (byte)'S',
      1,
      0, 0, 0, 0, 0, 0,
      3,
      480,
      640,
      8,
      3
    );

    Span<byte> buffer = stackalloc byte[PsdHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = PsdHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[PsdHeader.StructSize];
    data[0] = (byte)'8';
    data[1] = (byte)'B';
    data[2] = (byte)'P';
    data[3] = (byte)'S';
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(4), 1);       // version
    // reserved bytes 6-11 are zero
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(12), 4);      // channels
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(14), 1080);   // height
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(18), 1920);   // width
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(22), 16);     // depth
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(24), 3);      // color mode: RGB

    var header = PsdHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Sig0, Is.EqualTo((byte)'8'));
      Assert.That(header.Sig1, Is.EqualTo((byte)'B'));
      Assert.That(header.Sig2, Is.EqualTo((byte)'P'));
      Assert.That(header.Sig3, Is.EqualTo((byte)'S'));
      Assert.That(header.Version, Is.EqualTo(1));
      Assert.That(header.Channels, Is.EqualTo(4));
      Assert.That(header.Height, Is.EqualTo(1080));
      Assert.That(header.Width, Is.EqualTo(1920));
      Assert.That(header.Depth, Is.EqualTo(16));
      Assert.That(header.ColorMode, Is.EqualTo(3));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = PsdHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(PsdHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = PsdHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is26() {
    Assert.That(PsdHeader.StructSize, Is.EqualTo(26));
  }
}
