using System;
using System.IO;
using FileFormat.CpcSprite;

namespace FileFormat.CpcSprite.Tests;

[TestFixture]
public sealed class CpcSpriteReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcSpriteReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcSpriteReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cps"));
    Assert.Throws<FileNotFoundException>(() => CpcSpriteReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcSpriteReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => CpcSpriteReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[100];
    Assert.Throws<InvalidDataException>(() => CpcSpriteReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[CpcSpriteFile.ExpectedFileSize];

    var result = CpcSpriteReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(CpcSpriteFile.PixelWidth));
    Assert.That(result.Height, Is.EqualTo(CpcSpriteFile.PixelHeight));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_RawDataLengthMatchesExpected() {
    var data = new byte[CpcSpriteFile.ExpectedFileSize];

    var result = CpcSpriteReader.FromBytes(data);

    Assert.That(result.RawData.Length, Is.EqualTo(CpcSpriteFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_CopiesData() {
    var data = new byte[CpcSpriteFile.ExpectedFileSize];
    data[0] = 0xAA;
    data[63] = 0xBB;

    var result = CpcSpriteReader.FromBytes(data);

    Assert.That(result.RawData[0], Is.EqualTo(0xAA));
    Assert.That(result.RawData[63], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ClonesInputArray() {
    var data = new byte[CpcSpriteFile.ExpectedFileSize];
    data[0] = 0xAA;

    var result = CpcSpriteReader.FromBytes(data);

    Assert.That(result.RawData, Is.Not.SameAs(data));
    Assert.That(result.RawData[0], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[CpcSpriteFile.ExpectedFileSize];
    data[0] = 0xCC;

    using var stream = new MemoryStream(data);
    var result = CpcSpriteReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(CpcSpriteFile.PixelWidth));
    Assert.That(result.RawData[0], Is.EqualTo(0xCC));
  }
}
