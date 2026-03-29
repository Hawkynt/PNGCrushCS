using System;
using FileFormat.Art;

namespace FileFormat.Art.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ArtAnimType_HasExpectedValues() {
    Assert.That((int)ArtAnimType.None, Is.EqualTo(0));
    Assert.That((int)ArtAnimType.Oscillate, Is.EqualTo(1));
    Assert.That((int)ArtAnimType.Forward, Is.EqualTo(2));
    Assert.That((int)ArtAnimType.Backward, Is.EqualTo(3));

    var values = Enum.GetValues<ArtAnimType>();
    Assert.That(values, Has.Length.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ArtTile_DefaultValues() {
    var tile = new ArtTile();

    Assert.That(tile.Width, Is.EqualTo(0));
    Assert.That(tile.Height, Is.EqualTo(0));
    Assert.That(tile.AnimType, Is.EqualTo(ArtAnimType.None));
    Assert.That(tile.NumFrames, Is.EqualTo(0));
    Assert.That(tile.XOffset, Is.EqualTo(0));
    Assert.That(tile.YOffset, Is.EqualTo(0));
    Assert.That(tile.AnimSpeed, Is.EqualTo(0));
    Assert.That(tile.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ArtFile_DefaultValues() {
    var file = new ArtFile();

    Assert.That(file.TileStart, Is.EqualTo(0));
    Assert.That(file.Tiles, Is.Empty);
  }
}
