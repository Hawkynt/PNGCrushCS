using System;
using System.IO;
using FileFormat.Mcs;

namespace FileFormat.Mcs.Tests;

[TestFixture]
public sealed class McsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => McsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => McsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mcs"));
    Assert.Throws<FileNotFoundException>(() => McsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => McsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => McsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesLoadAddress() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = McsReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBitmapData() {
    var data = _BuildValidFile(0x4000, 0x03);
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = McsReader.FromBytes(data);

    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesScreenData() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = McsReader.FromBytes(data);

    Assert.That(result.ScreenData.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesColorData() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = McsReader.FromBytes(data);

    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBackgroundColor() {
    var data = _BuildValidFile(0x4000, 0x07);
    var result = McsReader.FromBytes(data);

    Assert.That(result.BackgroundColor, Is.EqualTo(0x07));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = _BuildValidFile(0x6000, 0x00);
    var result = McsReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_ParsesCorrectly() {
    var data = _BuildValidFile(0x4000, 0x05);
    using var ms = new MemoryStream(data);
    var result = McsReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenData.Length, Is.EqualTo(1000));
    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithTrailingData_ParsesTrailingBytes() {
    var data = new byte[McsFile.MinFileSize + 3];
    data[0] = 0x00;
    data[1] = 0x40;
    data[10002] = 0x05; // background color
    data[10003] = 0xAA;
    data[10004] = 0xBB;
    data[10005] = 0xCC;

    var result = McsReader.FromBytes(data);

    Assert.That(result.TrailingData.Length, Is.EqualTo(3));
    Assert.That(result.TrailingData[0], Is.EqualTo(0xAA));
    Assert.That(result.TrailingData[1], Is.EqualTo(0xBB));
    Assert.That(result.TrailingData[2], Is.EqualTo(0xCC));
  }

  private static byte[] _BuildValidFile(ushort loadAddress, byte backgroundColor) {
    var data = new byte[McsFile.MinFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    for (var i = 0; i < 1000; ++i)
      data[8002 + i] = (byte)(i % 16);

    for (var i = 0; i < 1000; ++i)
      data[9002 + i] = (byte)((i + 3) % 16);

    data[10002] = backgroundColor;

    return data;
  }
}
