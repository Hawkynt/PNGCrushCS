using System;
using System.IO;
using FileFormat.GunPaint;

namespace FileFormat.GunPaint.Tests;

[TestFixture]
public sealed class GunPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GunPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GunPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gun"));
    Assert.Throws<FileNotFoundException>(() => GunPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GunPaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => GunPaintReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[33604];
    Assert.Throws<InvalidDataException>(() => GunPaintReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesLoadAddress() {
    var data = _BuildValidFile(0x4000);
    var result = GunPaintReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesDimensions() {
    var data = _BuildValidFile(0x4000);
    var result = GunPaintReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_RawDataHasCorrectLength() {
    var data = _BuildValidFile(0x4000);
    var result = GunPaintReader.FromBytes(data);

    Assert.That(result.RawData.Length, Is.EqualTo(GunPaintFile.RawDataSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = new byte[GunPaintFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x40; // 0x4000 LE
    var result = GunPaintReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RawData_CopiedCorrectly() {
    var data = _BuildValidFile(0x4000);
    data[2] = 0xAB;
    data[33602] = 0xCD;

    var result = GunPaintReader.FromBytes(data);

    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[GunPaintFile.RawDataSize - 1], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_ParsesCorrectly() {
    var data = _BuildValidFile(0x4000);
    using var ms = new MemoryStream(data);
    var result = GunPaintReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  private static byte[] _BuildValidFile(ushort loadAddress) {
    var data = new byte[GunPaintFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 2; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    return data;
  }
}
