using System;
using FileFormat.Art;

namespace FileFormat.Art.Tests;

[TestFixture]
public sealed class ArtHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is16() {
    Assert.That(ArtHeader.StructSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new ArtHeader(1, 10, 0, 9);
    var buffer = new byte[ArtHeader.StructSize];
    original.WriteTo(buffer);

    var restored = ArtHeader.ReadFrom(buffer);

    Assert.That(restored.Version, Is.EqualTo(1));
    Assert.That(restored.NumTiles, Is.EqualTo(10));
    Assert.That(restored.TileStart, Is.EqualTo(0));
    Assert.That(restored.TileEnd, Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_HasFourEntries() {
    var fields = ArtHeader.GetFieldMap();

    Assert.That(fields, Has.Length.EqualTo(4));
    Assert.That(fields[0].Name, Is.EqualTo("Version"));
    Assert.That(fields[0].Offset, Is.EqualTo(0));
    Assert.That(fields[0].Size, Is.EqualTo(4));
    Assert.That(fields[1].Name, Is.EqualTo("NumTiles"));
    Assert.That(fields[2].Name, Is.EqualTo("TileStart"));
    Assert.That(fields[3].Name, Is.EqualTo("TileEnd"));
  }
}
