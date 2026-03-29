using System;
using System.IO;
using FileFormat.Artist64;

namespace FileFormat.Artist64.Tests;

[TestFixture]
public sealed class Artist64ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Artist64Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Artist64Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".a64"));
    Assert.Throws<FileNotFoundException>(() => Artist64Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Artist64Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => Artist64Reader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => Artist64Reader.FromBytes(new byte[10243]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidFile(0x4000);
    var result = Artist64Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.VideoMatrix.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = new byte[Artist64File.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x40;
    var result = Artist64Reader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BitmapData_CopiedCorrectly() {
    var data = _BuildValidFile(0x4000);
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = Artist64Reader.FromBytes(data);

    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidFile(0x4000);
    using var ms = new MemoryStream(data);
    var result = Artist64Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var videoMatrix = new byte[1000];
    for (var i = 0; i < videoMatrix.Length; ++i)
      videoMatrix[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var original = new Artist64File {
      LoadAddress = 0x4000,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
    };

    var bytes = Artist64Writer.ToBytes(original);
    var restored = Artist64Reader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.VideoMatrix, Is.EqualTo(original.VideoMatrix));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
  }

  private static byte[] _BuildValidFile(ushort loadAddress) {
    var data = new byte[Artist64File.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    for (var i = 0; i < 1000; ++i)
      data[8002 + i] = (byte)(i % 16);

    for (var i = 0; i < 1000; ++i)
      data[9002 + i] = (byte)((i + 3) % 16);

    return data;
  }
}
