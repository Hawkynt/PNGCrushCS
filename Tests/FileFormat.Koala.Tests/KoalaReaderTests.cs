using System;
using System.IO;
using FileFormat.Koala;

namespace FileFormat.Koala.Tests;

[TestFixture]
public sealed class KoalaReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KoalaReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KoalaReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".koa"));
    Assert.Throws<FileNotFoundException>(() => KoalaReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KoalaReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => KoalaReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[10004];
    Assert.Throws<InvalidDataException>(() => KoalaReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidKoalaFile(0x6000, 0x03);
    var result = KoalaReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.VideoMatrix.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x03));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = new byte[KoalaFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x60; // 0x6000 LE
    var result = KoalaReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BitmapData_CopiedCorrectly() {
    var data = _BuildValidKoalaFile(0x6000, 0x00);
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = KoalaReader.FromBytes(data);

    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidKoalaFile(0x6000, 0x05);
    using var ms = new MemoryStream(data);
    var result = KoalaReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }

  private static byte[] _BuildValidKoalaFile(ushort loadAddress, byte backgroundColor) {
    var data = new byte[KoalaFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    // Fill bitmap data with a recognizable pattern
    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    // Fill video matrix
    for (var i = 0; i < 1000; ++i)
      data[8002 + i] = (byte)(i % 16);

    // Fill color RAM
    for (var i = 0; i < 1000; ++i)
      data[9002 + i] = (byte)((i + 3) % 16);

    data[10002] = backgroundColor;

    return data;
  }
}
