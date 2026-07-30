using System;
using System.IO;
using FileFormat.CpcPlus;

namespace FileFormat.CpcPlus.Tests;

[TestFixture]
public sealed class CpcPlusReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcPlusReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcPlusReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpp"));
    Assert.Throws<FileNotFoundException>(() => CpcPlusReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcPlusReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => CpcPlusReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[20000];
    Assert.Throws<InvalidDataException>(() => CpcPlusReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ScreenDataOnly_ThrowsInvalidDataException() {
    var screenOnly = new byte[CpcPlusFile.ScreenDataSize];
    Assert.Throws<InvalidDataException>(() => CpcPlusReader.FromBytes(screenOnly));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[CpcPlusFile.ExpectedFileSize];

    var result = CpcPlusReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(CpcPlusFile.PixelWidth));
    Assert.That(result.Height, Is.EqualTo(CpcPlusFile.PixelHeight));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_PixelDataLengthMatchesLinearSize() {
    var data = new byte[CpcPlusFile.ExpectedFileSize];

    var result = CpcPlusReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesPaletteData() {
    var data = new byte[CpcPlusFile.ExpectedFileSize];
    // Set palette bytes at offset 16384
    data[CpcPlusFile.ScreenDataSize] = 0x0F;
    data[CpcPlusFile.ScreenDataSize + 1] = 0x0A;
    data[CpcPlusFile.ScreenDataSize + 2] = 0x05;
    data[CpcPlusFile.ScreenDataSize + 3] = 0x00;

    var result = CpcPlusReader.FromBytes(data);

    Assert.That(result.PaletteData.Length, Is.EqualTo(CpcPlusFile.PaletteDataSize));
    Assert.That(result.PaletteData[0], Is.EqualTo(0x0F));
    Assert.That(result.PaletteData[1], Is.EqualTo(0x0A));
    Assert.That(result.PaletteData[2], Is.EqualTo(0x05));
    Assert.That(result.PaletteData[3], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_DeinterleavesPixelData() {
    var data = new byte[CpcPlusFile.ExpectedFileSize];
    // Write at CPC address for line 0, column 0
    data[0] = 0xAA;
    // Line 1 address = ((1/8)*80) + ((1%8)*2048) = 2048
    data[2048] = 0xBB;

    var result = CpcPlusReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[CpcPlusFile.BytesPerRow], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[CpcPlusFile.ExpectedFileSize];
    data[0] = 0xCC;
    data[CpcPlusFile.ScreenDataSize] = 0x0F;

    using var stream = new MemoryStream(data);
    var result = CpcPlusReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(CpcPlusFile.PixelWidth));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCC));
    Assert.That(result.PaletteData[0], Is.EqualTo(0x0F));
  }
}
