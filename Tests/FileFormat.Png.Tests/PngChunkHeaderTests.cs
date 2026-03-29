using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Png.Tests;

[TestFixture]
public sealed class PngChunkHeaderTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = PngChunkHeader.Create(1234, "IHDR");
    Span<byte> buffer = stackalloc byte[PngChunkHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = PngChunkHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[PngChunkHeader.StructSize];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), 5678);
    data[4] = (byte)'I';
    data[5] = (byte)'D';
    data[6] = (byte)'A';
    data[7] = (byte)'T';

    var header = PngChunkHeader.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(header.Length, Is.EqualTo(5678));
      Assert.That(header.TypeByte0, Is.EqualTo((byte)'I'));
      Assert.That(header.TypeByte1, Is.EqualTo((byte)'D'));
      Assert.That(header.TypeByte2, Is.EqualTo((byte)'A'));
      Assert.That(header.TypeByte3, Is.EqualTo((byte)'T'));
    });
  }

  [Test]
  public void Type_ReturnsCorrectString() {
    var header = PngChunkHeader.Create(100, "PLTE");
    Assert.That(header.Type, Is.EqualTo("PLTE"));
  }

  [Test]
  public void Create_SetsTypeBytes() {
    var header = PngChunkHeader.Create(42, "tRNS");
    Assert.Multiple(() => {
      Assert.That(header.Length, Is.EqualTo(42));
      Assert.That(header.TypeByte0, Is.EqualTo((byte)'t'));
      Assert.That(header.TypeByte1, Is.EqualTo((byte)'R'));
      Assert.That(header.TypeByte2, Is.EqualTo((byte)'N'));
      Assert.That(header.TypeByte3, Is.EqualTo((byte)'S'));
    });
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = PngChunkHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(PngChunkHeader.StructSize));
  }

  [Test]
  public void StructSize_Is8() {
    Assert.That(PngChunkHeader.StructSize, Is.EqualTo(8));
  }
}
