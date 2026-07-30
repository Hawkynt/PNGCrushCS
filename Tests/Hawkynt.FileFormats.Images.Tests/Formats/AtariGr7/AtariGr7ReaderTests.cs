using System;
using System.IO;
using FileFormat.AtariGr7;

namespace FileFormat.AtariGr7.Tests;

[TestFixture]
public sealed class AtariGr7ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr7Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr7Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gr7"));
    Assert.Throws<FileNotFoundException>(() => AtariGr7Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr7Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AtariGr7Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[3841];
    Assert.Throws<InvalidDataException>(() => AtariGr7Reader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[3840];
    data[0] = 0b11100100;

    var result = AtariGr7Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(96));
    Assert.That(result.PixelData.Length, Is.EqualTo(160 * 96));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnpacksPixels_Correctly() {
    var data = new byte[3840];
    data[0] = 0b11100100;

    var result = AtariGr7Reader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(3));
    Assert.That(result.PixelData[1], Is.EqualTo(2));
    Assert.That(result.PixelData[2], Is.EqualTo(1));
    Assert.That(result.PixelData[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HasDefaultPalette() {
    var data = new byte[3840];

    var result = AtariGr7Reader.FromBytes(data);

    Assert.That(result.Palette, Is.Not.Null);
    Assert.That(result.Palette.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[3840];
    data[0] = 0xFF;

    using var ms = new MemoryStream(data);
    var result = AtariGr7Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(96));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AllZeroByte_UnpacksToZeros() {
    var data = new byte[3840];

    var result = AtariGr7Reader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(0));
    Assert.That(result.PixelData[2], Is.EqualTo(0));
    Assert.That(result.PixelData[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AllMaxByte_UnpacksToThrees() {
    var data = new byte[3840];
    data[0] = 0xFF;

    var result = AtariGr7Reader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(3));
    Assert.That(result.PixelData[1], Is.EqualTo(3));
    Assert.That(result.PixelData[2], Is.EqualTo(3));
    Assert.That(result.PixelData[3], Is.EqualTo(3));
  }
}
