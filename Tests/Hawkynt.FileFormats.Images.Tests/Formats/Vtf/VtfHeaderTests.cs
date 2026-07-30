using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Vtf;
using FileFormat.Core;

namespace FileFormat.Vtf.Tests;

[TestFixture]
public sealed class VtfHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new VtfHeader(
      Sig0: (byte)'V',
      Sig1: (byte)'T',
      Sig2: (byte)'F',
      Sig3: 0,
      VersionMajor: 7,
      VersionMinor: 2,
      HeaderSize: 64,
      Width: 256,
      Height: 128,
      Flags: 0x12,
      Frames: 3,
      FirstFrame: 0,
      Padding0: 0,
      ReflectivityR: 0.5f,
      ReflectivityG: 0.6f,
      ReflectivityB: 0.7f,
      Padding1: 0,
      BumpmapScale: 1.0f,
      HighResFormat: (int)VtfFormat.Rgba8888,
      MipmapCount: 5,
      LowResFormat: (int)VtfFormat.Dxt1,
      LowResWidth: 16,
      LowResHeight: 16,
      Padding2: 0
    );

    var buffer = new byte[VtfHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = VtfHeader.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[VtfHeader.StructSize];
    data[0] = (byte)'V';
    data[1] = (byte)'T';
    data[2] = (byte)'F';
    data[3] = 0;
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 7);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 2);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 64);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(16), 512);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(18), 256);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 0x10);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(24), 2);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 0);
    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(32), 0.5f);
    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(48), 1.0f);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(52), (int)VtfFormat.Dxt5);
    data[56] = 4;
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(57), (int)VtfFormat.Dxt1);
    data[61] = 16;
    data[62] = 16;

    var header = VtfHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Sig0, Is.EqualTo((byte)'V'));
      Assert.That(header.Sig1, Is.EqualTo((byte)'T'));
      Assert.That(header.Sig2, Is.EqualTo((byte)'F'));
      Assert.That(header.Sig3, Is.EqualTo(0));
      Assert.That(header.VersionMajor, Is.EqualTo(7));
      Assert.That(header.VersionMinor, Is.EqualTo(2));
      Assert.That(header.HeaderSize, Is.EqualTo(64));
      Assert.That(header.Width, Is.EqualTo(512));
      Assert.That(header.Height, Is.EqualTo(256));
      Assert.That(header.Flags, Is.EqualTo(0x10));
      Assert.That(header.Frames, Is.EqualTo(2));
      Assert.That(header.FirstFrame, Is.EqualTo(0));
      Assert.That(header.ReflectivityR, Is.EqualTo(0.5f));
      Assert.That(header.BumpmapScale, Is.EqualTo(1.0f));
      Assert.That(header.HighResFormat, Is.EqualTo((int)VtfFormat.Dxt5));
      Assert.That(header.MipmapCount, Is.EqualTo(4));
      Assert.That(header.LowResFormat, Is.EqualTo((int)VtfFormat.Dxt1));
      Assert.That(header.LowResWidth, Is.EqualTo(16));
      Assert.That(header.LowResHeight, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = VtfHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(VtfHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = VtfHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is64() {
    Assert.That(VtfHeader.StructSize, Is.EqualTo(64));
  }
}
