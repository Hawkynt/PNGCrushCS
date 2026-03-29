using System;
using System.IO;
using FileFormat.AppleIIgs;

namespace FileFormat.AppleIIgs.Tests;

[TestFixture]
public sealed class AppleIIgsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIgsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIgsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".shr"));
    Assert.Throws<FileNotFoundException>(() => AppleIIgsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIgsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AppleIIgsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[32769];
    Assert.Throws<InvalidDataException>(() => AppleIIgsReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode320_ParsesCorrectly() {
    var data = _BuildValidFile(AppleIIgsMode.Mode320);
    var result = AppleIIgsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Mode, Is.EqualTo(AppleIIgsMode.Mode320));
    Assert.That(result.PixelData.Length, Is.EqualTo(32000));
    Assert.That(result.Scbs.Length, Is.EqualTo(200));
    Assert.That(result.Palettes.Length, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode640_ParsesCorrectly() {
    var data = _BuildValidFile(AppleIIgsMode.Mode640);
    var result = AppleIIgsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Mode, Is.EqualTo(AppleIIgsMode.Mode640));
    Assert.That(result.PixelData.Length, Is.EqualTo(32000));
    Assert.That(result.Scbs.Length, Is.EqualTo(200));
    Assert.That(result.Palettes.Length, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelData_CopiedCorrectly() {
    var data = _BuildValidFile(AppleIIgsMode.Mode320);
    data[0] = 0xAB;
    data[31999] = 0xCD;

    var result = AppleIIgsReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[31999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidFile(AppleIIgsMode.Mode320);
    using var ms = new MemoryStream(data);
    var result = AppleIIgsReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Mode, Is.EqualTo(AppleIIgsMode.Mode320));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PaletteValues_ParsedAsLittleEndian() {
    var data = new byte[32768];
    // Palette starts at offset 32200 (32000 pixel + 200 SCB)
    // First palette entry at offset 32200: 0x34 0x12 -> 0x1234 LE
    data[32200] = 0x34;
    data[32201] = 0x12;

    var result = AppleIIgsReader.FromBytes(data);

    Assert.That(result.Palettes[0], Is.EqualTo(0x1234));
  }

  private static byte[] _BuildValidFile(AppleIIgsMode mode) {
    var data = new byte[32768];

    // Fill pixel data with a recognizable pattern
    for (var i = 0; i < 32000; ++i)
      data[i] = (byte)(i % 256);

    // Set SCBs with mode bit
    var scbValue = mode == AppleIIgsMode.Mode640 ? (byte)0x80 : (byte)0x00;
    for (var i = 0; i < 200; ++i)
      data[32000 + i] = scbValue;

    // Fill palette data with a pattern
    for (var i = 0; i < 512; ++i)
      data[32200 + i] = (byte)(i % 256);

    return data;
  }
}
