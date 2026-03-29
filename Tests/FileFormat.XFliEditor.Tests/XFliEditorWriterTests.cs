using System;
using FileFormat.XFliEditor;

namespace FileFormat.XFliEditor.Tests;

[TestFixture]
public sealed class XFliEditorWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XFliEditorWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_OutputIs17003Bytes() {
    var file = _BuildValidFile();
    var bytes = XFliEditorWriter.ToBytes(file);

    // LoadAddress(2) + Bitmap(8000) + 8*Screen(8000) + Color(1000) + BackgroundColor(1) = 17003
    Assert.That(bytes.Length, Is.EqualTo(17003));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_WrittenAsLittleEndian() {
    var file = _BuildMinimalFile(0x3C00);

    var bytes = XFliEditorWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x3C));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataOffset_StartsAtByte2() {
    var file = _BuildMinimalFile(0x3C00);
    file.BitmapData[0] = 0xAA;
    file.BitmapData[7999] = 0xBB;

    var bytes = XFliEditorWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[8001], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScreenBank0_StartsAtByte8002() {
    var file = _BuildMinimalFile(0x3C00);
    file.ScreenBanks[0][0] = 0xCC;
    file.ScreenBanks[0][999] = 0xDD;

    var bytes = XFliEditorWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xCC));
    Assert.That(bytes[9001], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScreenBank7_StartsAtByte15002() {
    var file = _BuildMinimalFile(0x3C00);
    file.ScreenBanks[7][0] = 0xEE;
    file.ScreenBanks[7][999] = 0xFF;

    var bytes = XFliEditorWriter.ToBytes(file);

    Assert.That(bytes[15002], Is.EqualTo(0xEE));
    Assert.That(bytes[16001], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorDataOffset_StartsAtByte16002() {
    var file = _BuildMinimalFile(0x3C00);
    file.ColorData[0] = 0x11;
    file.ColorData[999] = 0x22;

    var bytes = XFliEditorWriter.ToBytes(file);

    Assert.That(bytes[16002], Is.EqualTo(0x11));
    Assert.That(bytes[17001], Is.EqualTo(0x22));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BackgroundColor_AtByte17002() {
    var file = _BuildMinimalFile(0x3C00);
    file = new() {
      LoadAddress = file.LoadAddress,
      BitmapData = file.BitmapData,
      ScreenBanks = file.ScreenBanks,
      ColorData = file.ColorData,
      BackgroundColor = 9,
    };

    var bytes = XFliEditorWriter.ToBytes(file);

    Assert.That(bytes[17002], Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithTrailingData_IncludesTrailingBytes() {
    var file = new XFliEditorFile {
      LoadAddress = 0x3C00,
      BitmapData = new byte[8000],
      ScreenBanks = _MakeScreenBanks(),
      ColorData = new byte[1000],
      BackgroundColor = 0,
      TrailingData = [0xAA, 0xBB],
    };

    var bytes = XFliEditorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(17005));
    Assert.That(bytes[17003], Is.EqualTo(0xAA));
    Assert.That(bytes[17004], Is.EqualTo(0xBB));
  }

  private static XFliEditorFile _BuildMinimalFile(ushort loadAddress) => new() {
    LoadAddress = loadAddress,
    BitmapData = new byte[8000],
    ScreenBanks = _MakeScreenBanks(),
    ColorData = new byte[1000],
    BackgroundColor = 0,
  };

  private static byte[][] _MakeScreenBanks() {
    var banks = new byte[8][];
    for (var i = 0; i < 8; ++i)
      banks[i] = new byte[1000];
    return banks;
  }

  private static XFliEditorFile _BuildValidFile() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i % 256);

    var screenBanks = new byte[8][];
    for (var b = 0; b < 8; ++b) {
      screenBanks[b] = new byte[1000];
      for (var i = 0; i < 1000; ++i)
        screenBanks[b][i] = (byte)((i + b) % 16);
    }

    var colorData = new byte[1000];
    for (var i = 0; i < colorData.Length; ++i)
      colorData[i] = (byte)((i + 5) % 16);

    return new() {
      LoadAddress = 0x3C00,
      BitmapData = bitmapData,
      ScreenBanks = screenBanks,
      ColorData = colorData,
      BackgroundColor = 6,
    };
  }
}
