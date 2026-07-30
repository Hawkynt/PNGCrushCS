using System;
using System.IO;
using FileFormat.Atari8Bit;

namespace FileFormat.Atari8Bit.Tests;

[TestFixture]
public sealed class Atari8BitReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Atari8BitReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Atari8BitReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gr8"));
    Assert.Throws<FileNotFoundException>(() => Atari8BitReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Atari8BitReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => Atari8BitReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[5000];
    Assert.Throws<InvalidDataException>(() => Atari8BitReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGr8_ParsesDimensions() {
    var data = new byte[7680];
    data[0] = 0b10101010;

    var result = Atari8BitReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(Atari8BitMode.Gr8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGr8_UnpacksPixels() {
    var data = new byte[7680];
    data[0] = 0b10110001;

    var result = Atari8BitReader.FromBytes(data);

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
  public void FromBytes_ValidGr7_ParsesDimensions() {
    var data = new byte[1920];

    var result = Atari8BitReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(96));
    Assert.That(result.Mode, Is.EqualTo(Atari8BitMode.Gr7));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExplicitGr15_ParsesCorrectly() {
    var data = new byte[7680];
    data[0] = 0b11100100;

    var result = Atari8BitReader.FromBytes(data, Atari8BitMode.Gr15);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(Atari8BitMode.Gr15));
    Assert.That(result.PixelData[0], Is.EqualTo(3));
    Assert.That(result.PixelData[1], Is.EqualTo(2));
    Assert.That(result.PixelData[2], Is.EqualTo(1));
    Assert.That(result.PixelData[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExplicitGr9_ParsesCorrectly() {
    var data = new byte[7680];
    data[0] = 0xA5;

    var result = Atari8BitReader.FromBytes(data, Atari8BitMode.Gr9);

    Assert.That(result.Width, Is.EqualTo(80));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.Mode, Is.EqualTo(Atari8BitMode.Gr9));
    Assert.That(result.PixelData[0], Is.EqualTo(0x0A));
    Assert.That(result.PixelData[1], Is.EqualTo(0x05));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HasDefaultPalette() {
    var data = new byte[7680];

    var result = Atari8BitReader.FromBytes(data);

    Assert.That(result.Palette, Is.Not.Null);
    Assert.That(result.Palette.Length, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGr8() {
    var data = new byte[7680];
    using var ms = new MemoryStream(data);

    var result = Atari8BitReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(192));
  }
}
