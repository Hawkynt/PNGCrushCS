using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Farbfeld;
using FileFormat.Core;

namespace FileFormat.Farbfeld.Tests;

[TestFixture]
public sealed class FarbfeldHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new FarbfeldHeader(
      (byte)'f', (byte)'a', (byte)'r', (byte)'b',
      (byte)'f', (byte)'e', (byte)'l', (byte)'d',
      640, 480
    );
    Span<byte> buffer = stackalloc byte[FarbfeldHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = FarbfeldHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[16];
    data[0] = (byte)'f';
    data[1] = (byte)'a';
    data[2] = (byte)'r';
    data[3] = (byte)'b';
    data[4] = (byte)'f';
    data[5] = (byte)'e';
    data[6] = (byte)'l';
    data[7] = (byte)'d';
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 1920);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 1080);

    var header = FarbfeldHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Magic1, Is.EqualTo((byte)'f'));
      Assert.That(header.Magic2, Is.EqualTo((byte)'a'));
      Assert.That(header.Magic3, Is.EqualTo((byte)'r'));
      Assert.That(header.Magic4, Is.EqualTo((byte)'b'));
      Assert.That(header.Magic5, Is.EqualTo((byte)'f'));
      Assert.That(header.Magic6, Is.EqualTo((byte)'e'));
      Assert.That(header.Magic7, Is.EqualTo((byte)'l'));
      Assert.That(header.Magic8, Is.EqualTo((byte)'d'));
      Assert.That(header.Width, Is.EqualTo(1920));
      Assert.That(header.Height, Is.EqualTo(1080));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var header = new FarbfeldHeader(
      (byte)'f', (byte)'a', (byte)'r', (byte)'b',
      (byte)'f', (byte)'e', (byte)'l', (byte)'d',
      256, 128
    );
    var buffer = new byte[FarbfeldHeader.StructSize];
    header.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(buffer[0], Is.EqualTo((byte)'f'));
      Assert.That(buffer[1], Is.EqualTo((byte)'a'));
      Assert.That(buffer[2], Is.EqualTo((byte)'r'));
      Assert.That(buffer[3], Is.EqualTo((byte)'b'));
      Assert.That(buffer[4], Is.EqualTo((byte)'f'));
      Assert.That(buffer[5], Is.EqualTo((byte)'e'));
      Assert.That(buffer[6], Is.EqualTo((byte)'l'));
      Assert.That(buffer[7], Is.EqualTo((byte)'d'));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(8)), Is.EqualTo(256u));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(12)), Is.EqualTo(128u));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = FarbfeldHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(FarbfeldHeader.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = FarbfeldHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is16() {
    Assert.That(FarbfeldHeader.StructSize, Is.EqualTo(16));
  }
}
