using System;
using FileFormat.ChampionsInterlace;

namespace FileFormat.ChampionsInterlace.Tests;

[TestFixture]
public sealed class ChampionsInterlaceWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ChampionsInterlaceWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_OutputIs19003Bytes() {
    var file = _BuildValidFile();
    var bytes = ChampionsInterlaceWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(19003));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_WrittenAsLittleEndian() {
    var file = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = ChampionsInterlaceWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Bitmap1Offset_StartsAtByte2() {
    var file = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 0,
    };
    file.Bitmap1[0] = 0xAA;
    file.Bitmap1[7999] = 0xBB;

    var bytes = ChampionsInterlaceWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[8001], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Screen1Offset_StartsAtByte8002() {
    var file = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 0,
    };
    file.Screen1[0] = 0xCC;
    file.Screen1[999] = 0xDD;

    var bytes = ChampionsInterlaceWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xCC));
    Assert.That(bytes[9001], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorDataOffset_StartsAtByte9002() {
    var file = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 0,
    };
    file.ColorData[0] = 0xEE;
    file.ColorData[999] = 0xFF;

    var bytes = ChampionsInterlaceWriter.ToBytes(file);

    Assert.That(bytes[9002], Is.EqualTo(0xEE));
    Assert.That(bytes[10001], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Bitmap2Offset_StartsAtByte10002() {
    var file = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 0,
    };
    file.Bitmap2[0] = 0x11;
    file.Bitmap2[7999] = 0x22;

    var bytes = ChampionsInterlaceWriter.ToBytes(file);

    Assert.That(bytes[10002], Is.EqualTo(0x11));
    Assert.That(bytes[18001], Is.EqualTo(0x22));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Screen2Offset_StartsAtByte18002() {
    var file = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 0,
    };
    file.Screen2[0] = 0x33;
    file.Screen2[999] = 0x44;

    var bytes = ChampionsInterlaceWriter.ToBytes(file);

    Assert.That(bytes[18002], Is.EqualTo(0x33));
    Assert.That(bytes[19001], Is.EqualTo(0x44));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BackgroundColor_AtByte19002() {
    var file = new ChampionsInterlaceFile {
      LoadAddress = 0x4000,
      Bitmap1 = new byte[8000],
      Screen1 = new byte[1000],
      ColorData = new byte[1000],
      Bitmap2 = new byte[8000],
      Screen2 = new byte[1000],
      BackgroundColor = 6,
    };

    var bytes = ChampionsInterlaceWriter.ToBytes(file);

    Assert.That(bytes[19002], Is.EqualTo(6));
  }

  private static ChampionsInterlaceFile _BuildValidFile() {
    var bitmap1 = new byte[8000];
    for (var i = 0; i < bitmap1.Length; ++i)
      bitmap1[i] = (byte)(i % 256);

    var screen1 = new byte[1000];
    for (var i = 0; i < screen1.Length; ++i)
      screen1[i] = (byte)(i % 16);

    var colorData = new byte[1000];
    for (var i = 0; i < colorData.Length; ++i)
      colorData[i] = (byte)((i + 5) % 16);

    var bitmap2 = new byte[8000];
    for (var i = 0; i < bitmap2.Length; ++i)
      bitmap2[i] = (byte)((i + 1) % 256);

    var screen2 = new byte[1000];
    for (var i = 0; i < screen2.Length; ++i)
      screen2[i] = (byte)((i + 7) % 16);

    return new() {
      LoadAddress = 0x4000,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      ColorData = colorData,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
      BackgroundColor = 6,
    };
  }
}
