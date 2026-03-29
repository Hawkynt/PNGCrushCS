using System;
using FileFormat.Mcs;

namespace FileFormat.Mcs.Tests;

[TestFixture]
public sealed class McsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => McsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_OutputIs10003Bytes() {
    var file = _BuildValidFile();
    var bytes = McsWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(10003));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_WrittenAsLittleEndian() {
    var file = new McsFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = McsWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataOffset_StartsAtByte2() {
    var file = new McsFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 0,
    };
    file.BitmapData[0] = 0xAA;
    file.BitmapData[7999] = 0xBB;

    var bytes = McsWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[8001], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScreenDataOffset_StartsAtByte8002() {
    var file = new McsFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 0,
    };
    file.ScreenData[0] = 0xCC;
    file.ScreenData[999] = 0xDD;

    var bytes = McsWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xCC));
    Assert.That(bytes[9001], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorDataOffset_StartsAtByte9002() {
    var file = new McsFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 0,
    };
    file.ColorData[0] = 0xEE;
    file.ColorData[999] = 0xFF;

    var bytes = McsWriter.ToBytes(file);

    Assert.That(bytes[9002], Is.EqualTo(0xEE));
    Assert.That(bytes[10001], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BackgroundColor_AtByte10002() {
    var file = new McsFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 6,
    };

    var bytes = McsWriter.ToBytes(file);

    Assert.That(bytes[10002], Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithTrailingData_IncludesTrailingBytes() {
    var file = new McsFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 0,
      TrailingData = [0x11, 0x22],
    };

    var bytes = McsWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(10005));
    Assert.That(bytes[10003], Is.EqualTo(0x11));
    Assert.That(bytes[10004], Is.EqualTo(0x22));
  }

  private static McsFile _BuildValidFile() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i % 256);

    var screenData = new byte[1000];
    for (var i = 0; i < screenData.Length; ++i)
      screenData[i] = (byte)(i % 16);

    var colorData = new byte[1000];
    for (var i = 0; i < colorData.Length; ++i)
      colorData[i] = (byte)((i + 5) % 16);

    return new() {
      LoadAddress = 0x4000,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = 6,
    };
  }
}
