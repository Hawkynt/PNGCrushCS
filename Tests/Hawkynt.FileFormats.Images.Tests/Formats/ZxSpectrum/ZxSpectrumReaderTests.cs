using System;
using System.IO;
using FileFormat.ZxSpectrum;

namespace FileFormat.ZxSpectrum.Tests;

[TestFixture]
public sealed class ZxSpectrumReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxSpectrumReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxSpectrumReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".scr"));
    Assert.Throws<FileNotFoundException>(() => ZxSpectrumReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxSpectrumReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => ZxSpectrumReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[7000];
    Assert.Throws<InvalidDataException>(() => ZxSpectrumReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_OffByOne_ThrowsInvalidDataException() {
    var offByOne = new byte[6911];
    Assert.Throws<InvalidDataException>(() => ZxSpectrumReader.FromBytes(offByOne));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[6912];
    var result = ZxSpectrumReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
    Assert.That(result.AttributeData.Length, Is.EqualTo(768));
    Assert.That(result.BorderColor, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AttributeDataPreserved() {
    var data = new byte[6912];
    for (var i = 0; i < 768; ++i)
      data[6144 + i] = (byte)(i % 256);

    var result = ZxSpectrumReader.FromBytes(data);

    for (var i = 0; i < 768; ++i)
      Assert.That(result.AttributeData[i], Is.EqualTo((byte)(i % 256)));
  }
}
