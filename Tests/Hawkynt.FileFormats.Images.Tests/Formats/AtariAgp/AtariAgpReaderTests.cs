using System;
using System.IO;
using FileFormat.AtariAgp;

namespace FileFormat.AtariAgp.Tests;

[TestFixture]
public sealed class AtariAgpReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariAgpReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariAgpReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".agp"));
    Assert.Throws<FileNotFoundException>(() => AtariAgpReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariAgpReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AtariAgpReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnrecognizedSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[5000];
    Assert.Throws<InvalidDataException>(() => AtariAgpReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Gr8Size_ParsesAsGraphics8() {
    var data = new byte[7680];
    data[0] = 0x80;

    var result = AtariAgpReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(AtariAgpMode.Graphics8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Gr7Size_ParsesAsGraphics7() {
    var data = new byte[3840];
    data[0] = 0b11100100;

    var result = AtariAgpReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(96));
    Assert.That(result.Mode, Is.EqualTo(AtariAgpMode.Graphics7));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Gr8WithColors_ParsesCorrectly() {
    var data = new byte[7682];
    data[0] = 0x80;
    data[7680] = 0x12;
    data[7681] = 0x34;

    var result = AtariAgpReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(AtariAgpMode.Graphics8WithColors));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x12));
    Assert.That(result.ForegroundColor, Is.EqualTo(0x34));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Gr8_UnpacksPixels() {
    var data = new byte[7680];
    data[0] = 0b10110001;

    var result = AtariAgpReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(1));
    Assert.That(result.PixelData[1], Is.EqualTo(0));
    Assert.That(result.PixelData[2], Is.EqualTo(1));
    Assert.That(result.PixelData[3], Is.EqualTo(1));
    Assert.That(result.PixelData[4], Is.EqualTo(0));
    Assert.That(result.PixelData[5], Is.EqualTo(0));
    Assert.That(result.PixelData[6], Is.EqualTo(0));
    Assert.That(result.PixelData[7], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Gr7_UnpacksPixels() {
    var data = new byte[3840];
    data[0] = 0b11100100;

    var result = AtariAgpReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(3));
    Assert.That(result.PixelData[1], Is.EqualTo(2));
    Assert.That(result.PixelData[2], Is.EqualTo(1));
    Assert.That(result.PixelData[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGr8() {
    var data = new byte[7680];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = AtariAgpReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(AtariAgpMode.Graphics8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Gr8_HasDefaultPalette() {
    var data = new byte[7680];
    var result = AtariAgpReader.FromBytes(data);

    Assert.That(result.Palette, Is.Not.Null);
    Assert.That(result.Palette.Length, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Gr7_HasDefaultPalette() {
    var data = new byte[3840];
    var result = AtariAgpReader.FromBytes(data);

    Assert.That(result.Palette, Is.Not.Null);
    Assert.That(result.Palette.Length, Is.EqualTo(12));
  }
}
