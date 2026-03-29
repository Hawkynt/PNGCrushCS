using System;
using System.Linq;
using FileFormat.Acorn;
using FileFormat.Core;

namespace FileFormat.Acorn.Tests;

[TestFixture]
public sealed class AcornSpriteHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is44() {
    Assert.That(AcornSpriteHeader.StructSize, Is.EqualTo(44));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new AcornSpriteHeader(
      128,
      "testsprite",
      1,
      3,
      0,
      31,
      44,
      44,
      15
    );
    var buffer = new byte[AcornSpriteHeader.StructSize];
    original.WriteTo(buffer);

    var restored = AcornSpriteHeader.ReadFrom(buffer);

    Assert.That(restored.NextSpriteOffset, Is.EqualTo(128));
    Assert.That(restored.Name, Is.EqualTo("testsprite"));
    Assert.That(restored.WidthInWords, Is.EqualTo(1));
    Assert.That(restored.HeightInScanlines, Is.EqualTo(3));
    Assert.That(restored.FirstBitUsed, Is.EqualTo(0));
    Assert.That(restored.LastBitUsed, Is.EqualTo(31));
    Assert.That(restored.ImageOffset, Is.EqualTo(44));
    Assert.That(restored.MaskOffset, Is.EqualTo(44));
    Assert.That(restored.Mode, Is.EqualTo(15));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = AcornSpriteHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(AcornSpriteHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNineEntries() {
    var fields = AcornSpriteHeader.GetFieldMap();

    Assert.That(fields, Has.Length.EqualTo(9));
    Assert.That(fields[0].Name, Is.EqualTo("NextSpriteOffset"));
    Assert.That(fields[0].Offset, Is.EqualTo(0));
    Assert.That(fields[0].Size, Is.EqualTo(4));
    Assert.That(fields[1].Name, Is.EqualTo("Name"));
    Assert.That(fields[1].Size, Is.EqualTo(12));
    Assert.That(fields[8].Name, Is.EqualTo("Mode"));
  }
}
