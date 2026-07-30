using System;
using System.IO;
using FileFormat.Zoomatic;

namespace FileFormat.Zoomatic.Tests;

[TestFixture]
public sealed class ZoomaticReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZoomaticReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZoomaticReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zom"));
    Assert.Throws<FileNotFoundException>(() => ZoomaticReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ZoomaticReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => ZoomaticReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesLoadAddress() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBitmapData() {
    var data = _BuildValidFile(0x4000, 0x03);
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesScreenData() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.ScreenData.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesColorData() {
    var data = _BuildValidFile(0x4000, 0x03);
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBackgroundColor() {
    var data = _BuildValidFile(0x4000, 0x07);
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.BackgroundColor, Is.EqualTo(0x07));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = _BuildValidFile(0x6000, 0x00);
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_ParsesCorrectly() {
    var data = _BuildValidFile(0x4000, 0x05);
    using var ms = new MemoryStream(data);
    var result = ZoomaticReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenData.Length, Is.EqualTo(1000));
    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinPayloadOnly_BackgroundColorDefaultsToZero() {
    var data = new byte[ZoomaticFile.LoadAddressSize + ZoomaticFile.MinPayloadSize];
    var result = ZoomaticReader.FromBytes(data);

    Assert.That(result.BackgroundColor, Is.EqualTo(0));
  }

  private static byte[] _BuildValidFile(ushort loadAddress, byte backgroundColor) {
    // LoadAddress(2) + Bitmap(8000) + Screen(1000) + Color(1000) + BackgroundColor(1)
    var data = new byte[ZoomaticFile.LoadAddressSize + ZoomaticFile.MinPayloadSize + 1];
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
