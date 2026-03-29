using System;
using System.IO;
using FileFormat.AtariDrg;

namespace FileFormat.AtariDrg.Tests;

[TestFixture]
public sealed class AtariDrgReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariDrgReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariDrgReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".drg"));
    Assert.Throws<FileNotFoundException>(() => AtariDrgReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariDrgReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AtariDrgReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[7681];
    Assert.Throws<InvalidDataException>(() => AtariDrgReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[7680];
    data[0] = 0b11100100;

    var result = AtariDrgReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.PixelData.Length, Is.EqualTo(160 * 192));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnpacksPixels_Correctly() {
    var data = new byte[7680];
    data[0] = 0b11100100;

    var result = AtariDrgReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(3));
    Assert.That(result.PixelData[1], Is.EqualTo(2));
    Assert.That(result.PixelData[2], Is.EqualTo(1));
    Assert.That(result.PixelData[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HasDefaultPalette() {
    var data = new byte[7680];

    var result = AtariDrgReader.FromBytes(data);

    Assert.That(result.Palette, Is.Not.Null);
    Assert.That(result.Palette.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[7680];
    data[0] = 0xFF;

    using var ms = new MemoryStream(data);
    var result = AtariDrgReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AllZeroByte_UnpacksToZeros() {
    var data = new byte[7680];

    var result = AtariDrgReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(0));
    Assert.That(result.PixelData[2], Is.EqualTo(0));
    Assert.That(result.PixelData[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AllMaxByte_UnpacksToThrees() {
    var data = new byte[7680];
    data[0] = 0xFF;

    var result = AtariDrgReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(3));
    Assert.That(result.PixelData[1], Is.EqualTo(3));
    Assert.That(result.PixelData[2], Is.EqualTo(3));
    Assert.That(result.PixelData[3], Is.EqualTo(3));
  }
}
