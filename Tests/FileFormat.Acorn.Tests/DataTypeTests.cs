using System;
using FileFormat.Acorn;

namespace FileFormat.Acorn.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AcornSpriteMode_HasExpectedValues() {
    Assert.That((int)AcornSpriteMode.OneBpp, Is.EqualTo(1));
    Assert.That((int)AcornSpriteMode.TwoBpp, Is.EqualTo(2));
    Assert.That((int)AcornSpriteMode.FourBpp, Is.EqualTo(3));
    Assert.That((int)AcornSpriteMode.EightBpp, Is.EqualTo(4));
    Assert.That((int)AcornSpriteMode.SixteenBpp, Is.EqualTo(5));
    Assert.That((int)AcornSpriteMode.ThirtyTwoBpp, Is.EqualTo(6));

    var values = Enum.GetValues<AcornSpriteMode>();
    Assert.That(values, Has.Length.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void AcornFile_Defaults() {
    var file = new AcornFile();

    Assert.That(file.Sprites, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AcornSprite_Defaults() {
    var sprite = new AcornSprite();

    Assert.That(sprite.Name, Is.EqualTo(""));
    Assert.That(sprite.Width, Is.EqualTo(0));
    Assert.That(sprite.Height, Is.EqualTo(0));
    Assert.That(sprite.BitsPerPixel, Is.EqualTo(0));
    Assert.That(sprite.Mode, Is.EqualTo(0));
    Assert.That(sprite.PixelData, Is.Empty);
    Assert.That(sprite.MaskData, Is.Null);
    Assert.That(sprite.Palette, Is.Null);
  }
}
