using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Tga.Tests;

[TestFixture]
public sealed class TgaFooterTests {

  [Test]
  public void RoundTrip_PreservesAllFields() {
    var original = new TgaFooter(12345, 67890, TgaFooter.SignatureString);
    var buffer = new byte[TgaFooter.StructSize];
    original.WriteTo(buffer);
    var parsed = TgaFooter.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  public void ReadFrom_ParsesKnownValues() {
    var data = new byte[TgaFooter.StructSize];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 1024);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 2048);

    var footer = TgaFooter.ReadFrom(data);
    Assert.Multiple(() => {
      Assert.That(footer.ExtensionAreaOffset, Is.EqualTo(1024));
      Assert.That(footer.DeveloperDirectoryOffset, Is.EqualTo(2048));
    });
  }

  [Test]
  public void WriteTo_ProducesCorrectBytes() {
    var footer = new TgaFooter(4096, 8192, TgaFooter.SignatureString);
    var buffer = new byte[TgaFooter.StructSize];
    footer.WriteTo(buffer);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(0)), Is.EqualTo(4096));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(4)), Is.EqualTo(8192));
    });
  }

  [Test]
  public void WriteTo_WritesSignature() {
    var footer = new TgaFooter(0, 0, TgaFooter.SignatureString);
    var buffer = new byte[TgaFooter.StructSize];
    footer.WriteTo(buffer);

    var expected = "TRUEVISION-XFILE.\0"u8;
    var actual = buffer.AsSpan(8, 18);
    Assert.That(actual.SequenceEqual(expected), Is.True);
  }

  [Test]
  public void GetFieldMap_CoversFullStructSize() {
    var map = TgaFooter.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(TgaFooter.StructSize));
  }

  [Test]
  public void GetFieldMap_HasNoOverlaps() {
    var map = TgaFooter.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  public void StructSize_Is26() {
    Assert.That(TgaFooter.StructSize, Is.EqualTo(26));
  }
}
