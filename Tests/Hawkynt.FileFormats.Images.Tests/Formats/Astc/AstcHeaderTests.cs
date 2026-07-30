using System;
using System.Linq;
using FileFormat.Astc;
using FileFormat.Core;

namespace FileFormat.Astc.Tests;

[TestFixture]
public sealed class AstcHeaderTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new AstcHeader(
      Magic: AstcHeader.MagicValue,
      BlockDimX: 6,
      BlockDimY: 6,
      BlockDimZ: 1,
      Width: 1024,
      Height: 768,
      Depth: 1
    );

    var buffer = new byte[AstcHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = AstcHeader.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFullStructSize() {
    var map = AstcHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(AstcHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasNoOverlaps() {
    var map = AstcHeader.GetFieldMap();
    for (var i = 0; i < map.Length - 1; ++i)
      Assert.That(map[i].Offset + map[i].Size, Is.LessThanOrEqualTo(map[i + 1].Offset), $"Field {map[i].Name} overlaps with {map[i + 1].Name}");
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is16() {
    Assert.That(AstcHeader.StructSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void Uint24RoundTrip_LargeValues() {
    // Test uint24 encoding with a large dimension value (up to 16777215)
    var original = new AstcHeader(
      Magic: AstcHeader.MagicValue,
      BlockDimX: 4,
      BlockDimY: 4,
      BlockDimZ: 1,
      Width: 65535,
      Height: 131072,
      Depth: 256
    );

    var buffer = new byte[AstcHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = AstcHeader.ReadFrom(buffer);

    Assert.That(parsed.Width, Is.EqualTo(65535));
    Assert.That(parsed.Height, Is.EqualTo(131072));
    Assert.That(parsed.Depth, Is.EqualTo(256));
  }
}
