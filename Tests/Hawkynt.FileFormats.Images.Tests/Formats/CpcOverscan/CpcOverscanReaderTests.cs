using System;
using System.IO;
using FileFormat.CpcOverscan;

namespace FileFormat.CpcOverscan.Tests;

[TestFixture]
public sealed class CpcOverscanReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcOverscanReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcOverscanReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpo"));
    Assert.Throws<FileNotFoundException>(() => CpcOverscanReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcOverscanReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => CpcOverscanReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[40000];
    Assert.Throws<InvalidDataException>(() => CpcOverscanReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = new byte[CpcOverscanFile.ExpectedFileSize];

    var result = CpcOverscanReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(CpcOverscanFile.PixelWidth));
    Assert.That(result.Height, Is.EqualTo(CpcOverscanFile.PixelHeight));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_PixelDataLengthMatchesLinearSize() {
    var data = new byte[CpcOverscanFile.ExpectedFileSize];

    var result = CpcOverscanReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_DeinterleavesBank0Line0() {
    var data = new byte[CpcOverscanFile.ExpectedFileSize];
    // Bank 0, line 0: address = 0
    data[0] = 0xAA;

    var result = CpcOverscanReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_DeinterleavesBank0Line1() {
    var data = new byte[CpcOverscanFile.ExpectedFileSize];
    // Bank 0, line 1: address = ((1/8)*96) + ((1%8)*2048) = 0 + 2048 = 2048
    data[2048] = 0xBB;

    var result = CpcOverscanReader.FromBytes(data);

    // Linear line 1: offset = 1 * 96 = 96
    Assert.That(result.PixelData[CpcOverscanFile.BytesPerRow], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_DeinterleavesBank1Line0() {
    var data = new byte[CpcOverscanFile.ExpectedFileSize];
    // Bank 1, local line 0: bankOffset + ((0/8)*96) + ((0%8)*2048) = 16384 + 0 = 16384
    data[16384] = 0xCC;

    var result = CpcOverscanReader.FromBytes(data);

    // Linear line 136 (first line of bank 1): offset = 136 * 96 = 13056
    Assert.That(result.PixelData[136 * CpcOverscanFile.BytesPerRow], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[CpcOverscanFile.ExpectedFileSize];
    data[0] = 0xDD;

    using var stream = new MemoryStream(data);
    var result = CpcOverscanReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(CpcOverscanFile.PixelWidth));
    Assert.That(result.PixelData[0], Is.EqualTo(0xDD));
  }
}
