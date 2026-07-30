using System;
using System.IO;
using FileFormat.ZxBorderMulticolor;

namespace FileFormat.ZxBorderMulticolor.Tests;

[TestFixture]
public sealed class ZxBorderMulticolorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxBorderMulticolorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxBorderMulticolorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bmc4"));
    Assert.Throws<FileNotFoundException>(() => ZxBorderMulticolorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxBorderMulticolorReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[1000];
    Assert.Throws<InvalidDataException>(() => ZxBorderMulticolorReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_BitmapDataLength() {
    var data = new byte[ZxBorderMulticolorReader.FileSize];
    var result = ZxBorderMulticolorReader.FromBytes(data);

    Assert.That(result.BitmapData.Length, Is.EqualTo(ZxBorderMulticolorReader.BitmapSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_AttributeDataLength() {
    var data = new byte[ZxBorderMulticolorReader.FileSize];
    var result = ZxBorderMulticolorReader.FromBytes(data);

    Assert.That(result.AttributeData.Length, Is.EqualTo(ZxBorderMulticolorReader.AttributeSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_BorderDataLength() {
    var data = new byte[ZxBorderMulticolorReader.FileSize];
    var result = ZxBorderMulticolorReader.FromBytes(data);

    Assert.That(result.BorderData.Length, Is.EqualTo(ZxBorderMulticolorReader.BorderSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_FixedDimensions() {
    var data = new byte[ZxBorderMulticolorReader.FileSize];
    var result = ZxBorderMulticolorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AttributeDataPreserved() {
    var data = new byte[ZxBorderMulticolorReader.FileSize];
    // Set first attribute byte (right after bitmap data)
    data[ZxBorderMulticolorReader.BitmapSize] = 0x47;
    data[ZxBorderMulticolorReader.BitmapSize + 1] = 0x23;

    var result = ZxBorderMulticolorReader.FromBytes(data);

    Assert.That(result.AttributeData[0], Is.EqualTo(0x47));
    Assert.That(result.AttributeData[1], Is.EqualTo(0x23));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[ZxBorderMulticolorReader.FileSize];
    using var stream = new MemoryStream(data);

    var result = ZxBorderMulticolorReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }
}
