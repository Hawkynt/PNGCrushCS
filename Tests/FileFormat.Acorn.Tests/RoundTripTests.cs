using System;
using System.IO;
using FileFormat.Acorn;

namespace FileFormat.Acorn.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1bpp() {
    var sprite = _CreateTestSprite("mono", 32, 8, 1, 0);
    var original = new AcornFile { Sprites = [sprite] };

    var bytes = AcornWriter.ToBytes(original);
    var restored = AcornReader.FromBytes(bytes);

    Assert.That(restored.Sprites, Has.Count.EqualTo(1));
    Assert.That(restored.Sprites[0].Width, Is.EqualTo(32));
    Assert.That(restored.Sprites[0].Height, Is.EqualTo(8));
    Assert.That(restored.Sprites[0].BitsPerPixel, Is.EqualTo(1));
    Assert.That(restored.Sprites[0].PixelData, Is.EqualTo(sprite.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_4bpp() {
    var sprite = _CreateTestSprite("nibble", 16, 4, 4, 2);
    var original = new AcornFile { Sprites = [sprite] };

    var bytes = AcornWriter.ToBytes(original);
    var restored = AcornReader.FromBytes(bytes);

    Assert.That(restored.Sprites[0].Width, Is.EqualTo(16));
    Assert.That(restored.Sprites[0].Height, Is.EqualTo(4));
    Assert.That(restored.Sprites[0].BitsPerPixel, Is.EqualTo(4));
    Assert.That(restored.Sprites[0].PixelData, Is.EqualTo(sprite.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_8bpp() {
    var sprite = _CreateTestSprite("byte", 8, 4, 8, 15);
    var original = new AcornFile { Sprites = [sprite] };

    var bytes = AcornWriter.ToBytes(original);
    var restored = AcornReader.FromBytes(bytes);

    Assert.That(restored.Sprites[0].Width, Is.EqualTo(8));
    Assert.That(restored.Sprites[0].Height, Is.EqualTo(4));
    Assert.That(restored.Sprites[0].BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.Sprites[0].PixelData, Is.EqualTo(sprite.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_32bpp() {
    var sprite = _CreateTestSprite("truecolor", 8, 4, 32, 28);
    var original = new AcornFile { Sprites = [sprite] };

    var bytes = AcornWriter.ToBytes(original);
    var restored = AcornReader.FromBytes(bytes);

    Assert.That(restored.Sprites[0].Width, Is.EqualTo(8));
    Assert.That(restored.Sprites[0].Height, Is.EqualTo(4));
    Assert.That(restored.Sprites[0].BitsPerPixel, Is.EqualTo(32));
    Assert.That(restored.Sprites[0].PixelData, Is.EqualTo(sprite.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithMask() {
    var sprite = _CreateTestSpriteWithMask("masked", 8, 4, 8, 15);
    var original = new AcornFile { Sprites = [sprite] };

    var bytes = AcornWriter.ToBytes(original);
    var restored = AcornReader.FromBytes(bytes);

    Assert.That(restored.Sprites[0].PixelData, Is.EqualTo(sprite.PixelData));
    Assert.That(restored.Sprites[0].MaskData, Is.Not.Null);
    Assert.That(restored.Sprites[0].MaskData, Is.EqualTo(sprite.MaskData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPalette() {
    var sprite = _CreateTestSpriteWithPalette("palimg", 8, 4, 8, 15);
    var original = new AcornFile { Sprites = [sprite] };

    var bytes = AcornWriter.ToBytes(original);
    var restored = AcornReader.FromBytes(bytes);

    Assert.That(restored.Sprites[0].PixelData, Is.EqualTo(sprite.PixelData));
    Assert.That(restored.Sprites[0].Palette, Is.Not.Null);
    Assert.That(restored.Sprites[0].Palette, Is.EqualTo(sprite.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiSprite() {
    var s1 = _CreateTestSprite("first", 8, 4, 8, 15);
    var s2 = _CreateTestSprite("second", 16, 8, 8, 15);
    var original = new AcornFile { Sprites = [s1, s2] };

    var bytes = AcornWriter.ToBytes(original);
    var restored = AcornReader.FromBytes(bytes);

    Assert.That(restored.Sprites, Has.Count.EqualTo(2));
    Assert.That(restored.Sprites[0].Name, Is.EqualTo("first"));
    Assert.That(restored.Sprites[0].Width, Is.EqualTo(8));
    Assert.That(restored.Sprites[0].PixelData, Is.EqualTo(s1.PixelData));
    Assert.That(restored.Sprites[1].Name, Is.EqualTo("second"));
    Assert.That(restored.Sprites[1].Width, Is.EqualTo(16));
    Assert.That(restored.Sprites[1].PixelData, Is.EqualTo(s2.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var sprite = _CreateTestSprite("filert", 8, 4, 8, 15);
    var original = new AcornFile { Sprites = [sprite] };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spr");

    try {
      var bytes = AcornWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AcornReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Sprites, Has.Count.EqualTo(1));
      Assert.That(restored.Sprites[0].Name, Is.EqualTo("filert"));
      Assert.That(restored.Sprites[0].PixelData, Is.EqualTo(sprite.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
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

  private static AcornSprite _CreateTestSpriteWithMask(string name, int width, int height, int bpp, int mode) {
    var bytesPerRow = ((width * bpp + 31) / 32) * 4;
    var pixelData = new byte[bytesPerRow * height];
    var maskData = new byte[bytesPerRow * height];

    for (var i = 0; i < pixelData.Length; ++i) {
      pixelData[i] = (byte)(i % 256);
      maskData[i] = (byte)(255 - i % 256);
    }

    return new AcornSprite {
      Name = name,
      Width = width,
      Height = height,
      BitsPerPixel = bpp,
      Mode = mode,
      PixelData = pixelData,
      MaskData = maskData
    };
  }

  private static AcornSprite _CreateTestSpriteWithPalette(string name, int width, int height, int bpp, int mode) {
    var bytesPerRow = ((width * bpp + 31) / 32) * 4;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    // 256 entries, 2 words (8 bytes) each = 2048 bytes
    var paletteEntries = 1 << bpp;
    var palette = new byte[paletteEntries * 8];
    for (var i = 0; i < paletteEntries; ++i) {
      // First word: 0xBBGGRR00 format
      palette[i * 8 + 0] = 0;
      palette[i * 8 + 1] = (byte)i;
      palette[i * 8 + 2] = (byte)(255 - i);
      palette[i * 8 + 3] = (byte)(i * 2 % 256);
      // Second word: copy of first (flash pair)
      palette[i * 8 + 4] = palette[i * 8 + 0];
      palette[i * 8 + 5] = palette[i * 8 + 1];
      palette[i * 8 + 6] = palette[i * 8 + 2];
      palette[i * 8 + 7] = palette[i * 8 + 3];
    }

    return new AcornSprite {
      Name = name,
      Width = width,
      Height = height,
      BitsPerPixel = bpp,
      Mode = mode,
      PixelData = pixelData,
      Palette = palette
    };
  }
}
