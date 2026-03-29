using System;
using System.IO;
using FileFormat.HiresBitmap;

namespace FileFormat.HiresBitmap.Tests;

[TestFixture]
public sealed class HiresBitmapReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresBitmapReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_MissingFile_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hbm"));
    Assert.Throws<FileNotFoundException>(() => HiresBitmapReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_NullStream_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresBitmapReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresBitmapReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[9001];
    Assert.Throws<InvalidDataException>(() => HiresBitmapReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinSize_ParsesSuccessfully() {
    var data = new byte[9002];

    var result = HiresBitmapReader.FromBytes(data);

    Assert.That(result, Is.Not.Null);
    Assert.That(result.BitmapData.Length, Is.EqualTo(HiresBitmapFile.BitmapDataSize));
    Assert.That(result.ScreenData.Length, Is.EqualTo(HiresBitmapFile.ScreenDataSize));
    Assert.That(result.TrailingData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LargerSize_CapturesTrailingData() {
    var data = new byte[9010];

    var result = HiresBitmapReader.FromBytes(data);

    Assert.That(result.TrailingData.Length, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesLoadAddress_LittleEndian() {
    var data = new byte[9002];
    data[0] = 0x34;
    data[1] = 0x12;

    var result = HiresBitmapReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x1234));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesSuccessfully() {
    var data = new byte[9002];
    data[0] = 0xAB;
    data[1] = 0xCD;

    using var ms = new MemoryStream(data);
    var result = HiresBitmapReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0xCDAB));
    Assert.That(result.BitmapData.Length, Is.EqualTo(HiresBitmapFile.BitmapDataSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DataIsCopied() {
    var data = new byte[9002];
    data[2] = 0xFF;
    data[8002] = 0xAA;

    var result = HiresBitmapReader.FromBytes(data);
    data[2] = 0x00;
    data[8002] = 0x00;

    Assert.That(result.BitmapData[0], Is.EqualTo(0xFF));
    Assert.That(result.ScreenData[0], Is.EqualTo(0xAA));
  }
}
