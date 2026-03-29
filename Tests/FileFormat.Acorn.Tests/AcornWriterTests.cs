using System;
using System.Buffers.Binary;
using FileFormat.Acorn;

namespace FileFormat.Acorn.Tests;

[TestFixture]
public sealed class AcornWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AcornWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SpriteCount_WrittenCorrectly() {
    var sprite = _CreateTestSprite("count", 8, 2, 8, 15);
    var bytes = AcornWriter.ToBytes(new AcornFile { Sprites = [sprite] });

    var spriteCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0));
    Assert.That(spriteCount, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleSprites_CountCorrect() {
    var s1 = _CreateTestSprite("a", 8, 2, 8, 15);
    var s2 = _CreateTestSprite("b", 8, 2, 8, 15);
    var bytes = AcornWriter.ToBytes(new AcornFile { Sprites = [s1, s2] });

    var spriteCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0));
    Assert.That(spriteCount, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsName() {
    var sprite = _CreateTestSprite("mysprite", 8, 2, 8, 15);
    var bytes = AcornWriter.ToBytes(new AcornFile { Sprites = [sprite] });

    // Name starts at area header (12) + 4 bytes (NextSpriteOffset) = offset 16
    var name = System.Text.Encoding.ASCII.GetString(bytes, 16, 12).TrimEnd('\0');
    Assert.That(name, Is.EqualTo("mysprite"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FirstSpriteOffset_Is16() {
    var sprite = _CreateTestSprite("off", 8, 2, 8, 15);
    var bytes = AcornWriter.ToBytes(new AcornFile { Sprites = [sprite] });

    var firstSpriteOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
    Assert.That(firstSpriteOffset, Is.EqualTo(16));
  }

  private static AcornSprite _CreateTestSprite(string name, int width, int height, int bpp, int mode) {
    var bytesPerRow = ((width * bpp + 31) / 32) * 4;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    return new AcornSprite {
      Name = name,
      Width = width,
      Height = height,
      BitsPerPixel = bpp,
      Mode = mode,
      PixelData = pixelData
    };
  }
}
