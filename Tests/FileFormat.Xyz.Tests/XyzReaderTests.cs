using System;
using System.IO;
using FileFormat.Xyz;

namespace FileFormat.Xyz.Tests;

[TestFixture]
public sealed class XyzReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XyzReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XyzReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xyz"));
    Assert.Throws<FileNotFoundException>(() => XyzReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XyzReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => XyzReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    // Wrong magic bytes, correct size
    var bad = new byte[] { (byte)'X', (byte)'Y', (byte)'Z', (byte)'2', 0x01, 0x00, 0x01, 0x00, 0x78, 0x9C };
    Assert.Throws<InvalidDataException>(() => XyzReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    // Build a valid XYZ file: 2x2 with a known palette and pixel data
    var original = new XyzFile {
      Width = 2,
      Height = 2,
      Palette = _CreateTestPalette(),
      PixelData = [0, 1, 2, 3],
    };

    var bytes = XyzWriter.ToBytes(original);
    var result = XyzReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Palette.Length, Is.EqualTo(768));
    Assert.That(result.PixelData.Length, Is.EqualTo(4));
    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(1));
    Assert.That(result.PixelData[2], Is.EqualTo(2));
    Assert.That(result.PixelData[3], Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PaletteIsParsedCorrectly() {
    var palette = _CreateTestPalette();
    // Set palette entry 0 to red, entry 1 to green
    palette[0] = 255; palette[1] = 0; palette[2] = 0;
    palette[3] = 0; palette[4] = 255; palette[5] = 0;

    var original = new XyzFile {
      Width = 1,
      Height = 1,
      Palette = palette,
      PixelData = [0],
    };

    var bytes = XyzWriter.ToBytes(original);
    var result = XyzReader.FromBytes(bytes);

    Assert.That(result.Palette[0], Is.EqualTo(255));
    Assert.That(result.Palette[1], Is.EqualTo(0));
    Assert.That(result.Palette[2], Is.EqualTo(0));
    Assert.That(result.Palette[3], Is.EqualTo(0));
    Assert.That(result.Palette[4], Is.EqualTo(255));
    Assert.That(result.Palette[5], Is.EqualTo(0));
  }

  private static byte[] _CreateTestPalette() {
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)i;
      palette[i * 3 + 2] = (byte)i;
    }

    return palette;
  }
}
