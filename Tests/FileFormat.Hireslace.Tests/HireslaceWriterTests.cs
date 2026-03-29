using System;
using FileFormat.Hireslace;

namespace FileFormat.Hireslace.Tests;

[TestFixture]
public sealed class HireslaceWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HireslaceWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputSizeMatchesExpected() {
    var file = new HireslaceFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[HireslaceFile.BitmapDataSize],
      Screen1 = new byte[HireslaceFile.ScreenDataSize],
      Bitmap2 = new byte[HireslaceFile.BitmapDataSize],
      Screen2 = new byte[HireslaceFile.ScreenDataSize],
    };

    var bytes = HireslaceWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(HireslaceFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddressWrittenAsLE() {
    var file = new HireslaceFile {
      LoadAddress = 0x1234,
      Bitmap1 = new byte[HireslaceFile.BitmapDataSize],
      Screen1 = new byte[HireslaceFile.ScreenDataSize],
      Bitmap2 = new byte[HireslaceFile.BitmapDataSize],
      Screen2 = new byte[HireslaceFile.ScreenDataSize],
    };

    var bytes = HireslaceWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x34));
    Assert.That(bytes[1], Is.EqualTo(0x12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Bitmap1WrittenAfterLoadAddress() {
    var bitmap1 = new byte[HireslaceFile.BitmapDataSize];
    bitmap1[0] = 0xAA;
    bitmap1[1] = 0xBB;

    var file = new HireslaceFile {
      LoadAddress = 0x2000,
      Bitmap1 = bitmap1,
      Screen1 = new byte[HireslaceFile.ScreenDataSize],
      Bitmap2 = new byte[HireslaceFile.BitmapDataSize],
      Screen2 = new byte[HireslaceFile.ScreenDataSize],
    };

    var bytes = HireslaceWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[3], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Screen1WrittenAfterBitmap1() {
    var screen1 = new byte[HireslaceFile.ScreenDataSize];
    screen1[0] = 0xCC;

    var file = new HireslaceFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[HireslaceFile.BitmapDataSize],
      Screen1 = screen1,
      Bitmap2 = new byte[HireslaceFile.BitmapDataSize],
      Screen2 = new byte[HireslaceFile.ScreenDataSize],
    };

    var bytes = HireslaceWriter.ToBytes(file);

    var screen1Offset = HireslaceFile.LoadAddressSize + HireslaceFile.BitmapDataSize;
    Assert.That(bytes[screen1Offset], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Bitmap2WrittenAfterScreen1() {
    var bitmap2 = new byte[HireslaceFile.BitmapDataSize];
    bitmap2[0] = 0xDD;

    var file = new HireslaceFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[HireslaceFile.BitmapDataSize],
      Screen1 = new byte[HireslaceFile.ScreenDataSize],
      Bitmap2 = bitmap2,
      Screen2 = new byte[HireslaceFile.ScreenDataSize],
    };

    var bytes = HireslaceWriter.ToBytes(file);

    var bitmap2Offset = HireslaceFile.LoadAddressSize + HireslaceFile.BitmapDataSize + HireslaceFile.ScreenDataSize;
    Assert.That(bytes[bitmap2Offset], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Screen2WrittenAfterBitmap2() {
    var screen2 = new byte[HireslaceFile.ScreenDataSize];
    screen2[0] = 0xEE;

    var file = new HireslaceFile {
      LoadAddress = 0x2000,
      Bitmap1 = new byte[HireslaceFile.BitmapDataSize],
      Screen1 = new byte[HireslaceFile.ScreenDataSize],
      Bitmap2 = new byte[HireslaceFile.BitmapDataSize],
      Screen2 = screen2,
    };

    var bytes = HireslaceWriter.ToBytes(file);

    var screen2Offset = HireslaceFile.LoadAddressSize + HireslaceFile.BitmapDataSize + HireslaceFile.ScreenDataSize + HireslaceFile.BitmapDataSize;
    Assert.That(bytes[screen2Offset], Is.EqualTo(0xEE));
  }
}
