using System;
using System.IO;
using FileFormat.CpcAdvanced;

namespace FileFormat.CpcAdvanced.Tests;

[TestFixture]
public sealed class CpcAdvancedReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcAdvancedReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcAdvancedReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpa"));
    Assert.Throws<FileNotFoundException>(() => CpcAdvancedReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcAdvancedReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => CpcAdvancedReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[20000];
    Assert.Throws<InvalidDataException>(() => CpcAdvancedReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[CpcAdvancedFile.ExpectedFileSize];

    var result = CpcAdvancedReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(CpcAdvancedFile.PixelWidth));
    Assert.That(result.Height, Is.EqualTo(CpcAdvancedFile.PixelHeight));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_DeinterleavesPixelData() {
    var data = new byte[CpcAdvancedFile.ExpectedFileSize];
    // Write recognizable byte at CPC address for line 0, column 0
    data[0] = 0xAA;
    // Write recognizable byte at CPC address for line 1, column 0
    // Line 1 address = ((1/8)*80) + ((1%8)*2048) = 0 + 2048 = 2048
    data[2048] = 0xBB;

    var result = CpcAdvancedReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[CpcAdvancedFile.BytesPerRow], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_PixelDataLengthMatchesLinearSize() {
    var data = new byte[CpcAdvancedFile.ExpectedFileSize];

    var result = CpcAdvancedReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[CpcAdvancedFile.ExpectedFileSize];
    data[0] = 0xCC;

    using var stream = new MemoryStream(data);
    var result = CpcAdvancedReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(CpcAdvancedFile.PixelWidth));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCC));
  }
}
