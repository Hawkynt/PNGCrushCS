using System;
using FileFormat.C64Multi;

namespace FileFormat.C64Multi.Tests;

[TestFixture]
public sealed class C64MultiWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C64MultiWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Hires_OutputIsExactly9009Bytes() {
    var file = _BuildValidHiresFile();
    var bytes = C64MultiWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(C64MultiFile.ArtStudioHiresFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Multicolor_OutputIsExactly10018Bytes() {
    var file = _BuildValidMulticolorFile();
    var bytes = C64MultiWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(C64MultiFile.ArtStudioMultiFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Hires_LoadAddress_WrittenAsLittleEndian() {
    var file = new C64MultiFile {
      Width = 320,
      Height = 200,
      Format = C64MultiFormat.ArtStudioHires,
      LoadAddress = 0x2000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      BackgroundColor = 0
    };

    var bytes = C64MultiWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x20));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Multicolor_LoadAddress_WrittenAsLittleEndian() {
    var file = new C64MultiFile {
      Width = 160,
      Height = 200,
      Format = C64MultiFormat.ArtStudioMulti,
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 0
    };

    var bytes = C64MultiWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Hires_BitmapDataOffset_StartsAtByte2() {
    var file = new C64MultiFile {
      Width = 320,
      Height = 200,
      Format = C64MultiFormat.ArtStudioHires,
      LoadAddress = 0x2000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      BackgroundColor = 0
    };
    file.BitmapData[0] = 0xAA;
    file.BitmapData[7999] = 0xBB;

    var bytes = C64MultiWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[8001], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Hires_ScreenDataOffset_StartsAtByte8002() {
    var file = new C64MultiFile {
      Width = 320,
      Height = 200,
      Format = C64MultiFormat.ArtStudioHires,
      LoadAddress = 0x2000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      BackgroundColor = 0
    };
    file.ScreenData[0] = 0xCC;
    file.ScreenData[999] = 0xDD;

    var bytes = C64MultiWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xCC));
    Assert.That(bytes[9001], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Hires_BorderColor_AtByte9002() {
    var file = new C64MultiFile {
      Width = 320,
      Height = 200,
      Format = C64MultiFormat.ArtStudioHires,
      LoadAddress = 0x2000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      BackgroundColor = 14
    };

    var bytes = C64MultiWriter.ToBytes(file);

    Assert.That(bytes[9002], Is.EqualTo(14));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Multicolor_ColorDataOffset_StartsAtByte9002() {
    var file = new C64MultiFile {
      Width = 160,
      Height = 200,
      Format = C64MultiFormat.ArtStudioMulti,
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 0
    };
    file.ColorData![0] = 0xEE;
    file.ColorData[999] = 0xFF;

    var bytes = C64MultiWriter.ToBytes(file);

    Assert.That(bytes[9002], Is.EqualTo(0xEE));
    Assert.That(bytes[10001], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Multicolor_BackgroundColor_AtByte10002() {
    var file = new C64MultiFile {
      Width = 160,
      Height = 200,
      Format = C64MultiFormat.ArtStudioMulti,
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
      ColorData = new byte[1000],
      BackgroundColor = 6
    };

    var bytes = C64MultiWriter.ToBytes(file);

    Assert.That(bytes[10002], Is.EqualTo(6));
  }

  private static C64MultiFile _BuildValidHiresFile() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i % 256);

    var screenData = new byte[1000];
    for (var i = 0; i < screenData.Length; ++i)
      screenData[i] = (byte)(i % 256);

    return new() {
      Width = 320,
      Height = 200,
      Format = C64MultiFormat.ArtStudioHires,
      LoadAddress = 0x2000,
      BitmapData = bitmapData,
      ScreenData = screenData,
      BackgroundColor = 14
    };
  }

  private static C64MultiFile _BuildValidMulticolorFile() {
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
      Width = 160,
      Height = 200,
      Format = C64MultiFormat.ArtStudioMulti,
      LoadAddress = 0x4000,
      BitmapData = bitmapData,
      ScreenData = screenData,
      ColorData = colorData,
      BackgroundColor = 6
    };
  }
}
