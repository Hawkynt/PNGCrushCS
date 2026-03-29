using System;
using System.IO;
using FileFormat.ZxPaintbrush;

namespace FileFormat.ZxPaintbrush.Tests;

[TestFixture]
public sealed class ZxPaintbrushReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxPaintbrushReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxPaintbrushReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zxp"));
    Assert.Throws<FileNotFoundException>(() => ZxPaintbrushReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZxPaintbrushReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => ZxPaintbrushReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OffByOne_ThrowsInvalidDataException() {
    var offByOne = new byte[6911];
    Assert.Throws<InvalidDataException>(() => ZxPaintbrushReader.FromBytes(offByOne));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactMinSize_ParsesCorrectly() {
    var data = new byte[6912];
    var result = ZxPaintbrushReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.BitmapData.Length, Is.EqualTo(6144));
    Assert.That(result.AttributeData.Length, Is.EqualTo(768));
    Assert.That(result.ExtraData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithExtraData_ParsesExtraData() {
    var data = new byte[6912 + 100];
    for (var i = 0; i < 100; ++i)
      data[6912 + i] = (byte)(i + 1);

    var result = ZxPaintbrushReader.FromBytes(data);

    Assert.That(result.ExtraData.Length, Is.EqualTo(100));
    Assert.That(result.ExtraData[0], Is.EqualTo(1));
    Assert.That(result.ExtraData[99], Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AttributeDataPreserved() {
    var data = new byte[6912];
    for (var i = 0; i < 768; ++i)
      data[6144 + i] = (byte)(i % 256);

    var result = ZxPaintbrushReader.FromBytes(data);

    for (var i = 0; i < 768; ++i)
      Assert.That(result.AttributeData[i], Is.EqualTo((byte)(i % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[6912];
    using var stream = new MemoryStream(data);

    var result = ZxPaintbrushReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }
}
