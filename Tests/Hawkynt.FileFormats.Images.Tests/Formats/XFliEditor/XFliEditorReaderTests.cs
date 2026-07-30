using System;
using System.IO;
using FileFormat.XFliEditor;

namespace FileFormat.XFliEditor.Tests;

[TestFixture]
public sealed class XFliEditorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XFliEditorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XFliEditorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xfl"));
    Assert.Throws<FileNotFoundException>(() => XFliEditorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XFliEditorReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => XFliEditorReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesLoadAddress() {
    var data = _BuildValidFile(0x3C00, 0x00);
    var result = XFliEditorReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBitmapData() {
    var data = _BuildValidFile(0x3C00, 0x00);
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = XFliEditorReader.FromBytes(data);

    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_Parses8ScreenBanks() {
    var data = _BuildValidFile(0x3C00, 0x00);
    var result = XFliEditorReader.FromBytes(data);

    Assert.That(result.ScreenBanks.Length, Is.EqualTo(8));
    for (var i = 0; i < 8; ++i)
      Assert.That(result.ScreenBanks[i].Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesColorData() {
    var data = _BuildValidFile(0x3C00, 0x00);
    var result = XFliEditorReader.FromBytes(data);

    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBackgroundColor() {
    var data = _BuildValidFile(0x3C00, 0x07);
    var result = XFliEditorReader.FromBytes(data);

    Assert.That(result.BackgroundColor, Is.EqualTo(0x07));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = _BuildValidFile(0x6000, 0x00);
    var result = XFliEditorReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ScreenBanks_DataCopiedCorrectly() {
    var data = _BuildValidFile(0x3C00, 0x00);
    // Bank 0 starts at offset 2 + 8000 = 8002
    data[8002] = 0xAA;
    // Bank 7 starts at offset 2 + 8000 + 7*1000 = 15002
    data[15002] = 0xBB;

    var result = XFliEditorReader.FromBytes(data);

    Assert.That(result.ScreenBanks[0][0], Is.EqualTo(0xAA));
    Assert.That(result.ScreenBanks[7][0], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_ParsesCorrectly() {
    var data = _BuildValidFile(0x3C00, 0x05);
    using var ms = new MemoryStream(data);
    var result = XFliEditorReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenBanks.Length, Is.EqualTo(8));
    Assert.That(result.ColorData.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinPayloadOnly_BackgroundColorDefaultsToZero() {
    var data = new byte[XFliEditorFile.LoadAddressSize + XFliEditorFile.MinPayloadSize];
    var result = XFliEditorReader.FromBytes(data);

    Assert.That(result.BackgroundColor, Is.EqualTo(0));
  }

  private static byte[] _BuildValidFile(ushort loadAddress, byte backgroundColor) {
    // LoadAddress(2) + Bitmap(8000) + 8*Screen(8000) + Color(1000) + BackgroundColor(1)
    var data = new byte[XFliEditorFile.LoadAddressSize + XFliEditorFile.MinPayloadSize + 1];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    for (var bank = 0; bank < 8; ++bank)
      for (var i = 0; i < 1000; ++i)
        data[8002 + bank * 1000 + i] = (byte)((i + bank) % 16);

    for (var i = 0; i < 1000; ++i)
      data[16002 + i] = (byte)((i + 3) % 16);

    data[17002] = backgroundColor;

    return data;
  }
}
