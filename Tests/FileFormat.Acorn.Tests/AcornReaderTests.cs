using System;
using System.IO;
using FileFormat.Acorn;

namespace FileFormat.Acorn.Tests;

[TestFixture]
public sealed class AcornReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AcornReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AcornReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spr"));
    Assert.Throws<FileNotFoundException>(() => AcornReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AcornReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => AcornReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleSprite_ParsesCorrectly() {
    var sprite = _CreateTestSprite("test", 8, 4, 8, 15);
    var original = new AcornFile { Sprites = [sprite] };
    var bytes = AcornWriter.ToBytes(original);

    var result = AcornReader.FromBytes(bytes);

    Assert.That(result.Sprites, Has.Count.EqualTo(1));
    Assert.That(result.Sprites[0].Name, Is.EqualTo("test"));
    Assert.That(result.Sprites[0].Width, Is.EqualTo(8));
    Assert.That(result.Sprites[0].Height, Is.EqualTo(4));
    Assert.That(result.Sprites[0].BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidSprite_ParsesCorrectly() {
    var sprite = _CreateTestSprite("stream", 8, 2, 8, 15);
    var bytes = AcornWriter.ToBytes(new AcornFile { Sprites = [sprite] });

    using var ms = new MemoryStream(bytes);
    var result = AcornReader.FromStream(ms);

    Assert.That(result.Sprites, Has.Count.EqualTo(1));
    Assert.That(result.Sprites[0].Name, Is.EqualTo("stream"));
  }

  [Test]
  [Category("Unit")]
  public void GetBitsPerPixel_OldModes_ReturnsCorrectBpp() {
    Assert.That(AcornReader._GetBitsPerPixel(0), Is.EqualTo(1));
    Assert.That(AcornReader._GetBitsPerPixel(1), Is.EqualTo(2));
    Assert.That(AcornReader._GetBitsPerPixel(2), Is.EqualTo(4));
    Assert.That(AcornReader._GetBitsPerPixel(15), Is.EqualTo(8));
    Assert.That(AcornReader._GetBitsPerPixel(21), Is.EqualTo(16));
    Assert.That(AcornReader._GetBitsPerPixel(28), Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void GetBitsPerPixel_NewFormatMode_ExtractsFromModeWord() {
    // Mode word >= 256 with log2bpp in bits 27-30
    // log2bpp = 3 => bpp = 8, so bits 27-30 = 3 => 3 << 27 = 0x18000000
    var modeWord = 0x18000000 | 0x100; // ensure >= 256
    Assert.That(AcornReader._GetBitsPerPixel(modeWord), Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void GetBitsPerPixel_UnknownOldMode_Defaults8bpp() {
    Assert.That(AcornReader._GetBitsPerPixel(99), Is.EqualTo(8));
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
