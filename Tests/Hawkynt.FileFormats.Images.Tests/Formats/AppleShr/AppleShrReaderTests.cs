using System;
using System.IO;
using FileFormat.AppleShr;

namespace FileFormat.AppleShr.Tests;

[TestFixture]
public sealed class AppleShrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleShrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleShrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".shr"));
    Assert.Throws<FileNotFoundException>(() => AppleShrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleShrReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AppleShrReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[32769];
    Assert.Throws<InvalidDataException>(() => AppleShrReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidShrFile();
    var result = AppleShrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.PixelData.Length, Is.EqualTo(32000));
    Assert.That(result.ScanlineControl.Length, Is.EqualTo(200));
    Assert.That(result.Palette.Length, Is.EqualTo(512));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelData_CopiedCorrectly() {
    var data = _BuildValidShrFile();
    data[0] = 0xAB;
    data[31999] = 0xCD;

    var result = AppleShrReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[31999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ScanlineControl_CopiedCorrectly() {
    var data = _BuildValidShrFile();
    // SCB starts at offset 32000
    data[32000] = 0x05;
    data[32199] = 0x0A;

    var result = AppleShrReader.FromBytes(data);

    Assert.That(result.ScanlineControl[0], Is.EqualTo(0x05));
    Assert.That(result.ScanlineControl[199], Is.EqualTo(0x0A));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Palette_CopiedCorrectly() {
    var data = _BuildValidShrFile();
    // Palette starts at offset 32000 + 200 + 56 = 32256
    data[32256] = 0x12;
    data[32767] = 0x34;

    var result = AppleShrReader.FromBytes(data);

    Assert.That(result.Palette[0], Is.EqualTo(0x12));
    Assert.That(result.Palette[511], Is.EqualTo(0x34));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidShrFile();
    using var ms = new MemoryStream(data);
    var result = AppleShrReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.PixelData.Length, Is.EqualTo(32000));
  }

  private static byte[] _BuildValidShrFile() {
    var data = new byte[AppleShrFile.ExpectedFileSize];

    for (var i = 0; i < 32000; ++i)
      data[i] = (byte)(i % 256);

    for (var i = 0; i < 200; ++i)
      data[32000 + i] = (byte)(i % 16);

    for (var i = 0; i < 512; ++i)
      data[32256 + i] = (byte)(i % 256);

    return data;
  }
}
