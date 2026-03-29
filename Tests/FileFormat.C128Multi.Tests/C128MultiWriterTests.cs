using System;
using FileFormat.C128Multi;

namespace FileFormat.C128Multi.Tests;

[TestFixture]
public sealed class C128MultiWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128MultiWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesExpectedFileSize() {
    var file = new C128MultiFile {
      BitmapData = new byte[C128MultiFile.BitmapDataSize],
      ScreenData = new byte[C128MultiFile.ScreenDataSize],
      ColorData = new byte[C128MultiFile.ColorDataSize],
      BackgroundColor = 0,
    };

    var bytes = C128MultiWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(C128MultiFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataPreserved() {
    var bitmap = new byte[C128MultiFile.BitmapDataSize];
    bitmap[0] = 0xAB;
    bitmap[3999] = 0xCD;
    bitmap[7999] = 0xEF;

    var file = new C128MultiFile {
      BitmapData = bitmap,
      ScreenData = new byte[C128MultiFile.ScreenDataSize],
      ColorData = new byte[C128MultiFile.ColorDataSize],
      BackgroundColor = 0,
    };

    var bytes = C128MultiWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xAB));
    Assert.That(bytes[3999], Is.EqualTo(0xCD));
    Assert.That(bytes[7999], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ScreenDataAtCorrectOffset() {
    var screen = new byte[C128MultiFile.ScreenDataSize];
    screen[0] = 0x12;
    screen[999] = 0x34;

    var file = new C128MultiFile {
      BitmapData = new byte[C128MultiFile.BitmapDataSize],
      ScreenData = screen,
      ColorData = new byte[C128MultiFile.ColorDataSize],
      BackgroundColor = 0,
    };

    var bytes = C128MultiWriter.ToBytes(file);

    Assert.That(bytes[C128MultiFile.BitmapDataSize], Is.EqualTo(0x12));
    Assert.That(bytes[C128MultiFile.BitmapDataSize + 999], Is.EqualTo(0x34));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorDataAtCorrectOffset() {
    var color = new byte[C128MultiFile.ColorDataSize];
    color[0] = 0x56;
    color[999] = 0x78;

    var file = new C128MultiFile {
      BitmapData = new byte[C128MultiFile.BitmapDataSize],
      ScreenData = new byte[C128MultiFile.ScreenDataSize],
      ColorData = color,
      BackgroundColor = 0,
    };

    var bytes = C128MultiWriter.ToBytes(file);

    var colorOffset = C128MultiFile.BitmapDataSize + C128MultiFile.ScreenDataSize;
    Assert.That(bytes[colorOffset], Is.EqualTo(0x56));
    Assert.That(bytes[colorOffset + 999], Is.EqualTo(0x78));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BackgroundColorAtCorrectOffset() {
    var file = new C128MultiFile {
      BitmapData = new byte[C128MultiFile.BitmapDataSize],
      ScreenData = new byte[C128MultiFile.ScreenDataSize],
      ColorData = new byte[C128MultiFile.ColorDataSize],
      BackgroundColor = 14,
    };

    var bytes = C128MultiWriter.ToBytes(file);

    var bgOffset = C128MultiFile.BitmapDataSize + C128MultiFile.ScreenDataSize + C128MultiFile.ColorDataSize;
    Assert.That(bytes[bgOffset], Is.EqualTo(14));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SpareAreaZeroed() {
    var file = new C128MultiFile {
      BitmapData = new byte[C128MultiFile.BitmapDataSize],
      ScreenData = new byte[C128MultiFile.ScreenDataSize],
      ColorData = new byte[C128MultiFile.ColorDataSize],
      BackgroundColor = 5,
    };

    var bytes = C128MultiWriter.ToBytes(file);

    var spareStart = C128MultiFile.BitmapDataSize + C128MultiFile.ScreenDataSize + C128MultiFile.ColorDataSize + 1;
    for (var i = spareStart; i < C128MultiFile.ExpectedFileSize; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Spare byte at offset {i} should be zero");
  }
}
