using System;
using FileFormat.InterlaceStudio;

namespace FileFormat.InterlaceStudio.Tests;

[TestFixture]
public sealed class InterlaceStudioWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterlaceStudioWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces19003Bytes() {
    var file = _CreateValidFile();

    var bytes = InterlaceStudioWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(19003));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesLoadAddress_LittleEndian() {
    var file = _CreateValidFile(loadAddress: 0x1234);

    var bytes = InterlaceStudioWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x34));
    Assert.That(bytes[1], Is.EqualTo(0x12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesBitmap1() {
    var file = _CreateValidFile();
    file.Bitmap1[0] = 0xAB;

    var bytes = InterlaceStudioWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesScreen1() {
    var file = _CreateValidFile();
    file.Screen1[0] = 0xCD;

    var bytes = InterlaceStudioWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesColorData() {
    var file = _CreateValidFile();
    file.ColorData[0] = 0xEF;

    var bytes = InterlaceStudioWriter.ToBytes(file);

    Assert.That(bytes[9002], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesBitmap2() {
    var file = _CreateValidFile();
    file.Bitmap2[0] = 0x42;

    var bytes = InterlaceStudioWriter.ToBytes(file);

    Assert.That(bytes[10002], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesScreen2() {
    var file = _CreateValidFile();
    file.Screen2[0] = 0x99;

    var bytes = InterlaceStudioWriter.ToBytes(file);

    Assert.That(bytes[18002], Is.EqualTo(0x99));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesBackgroundColor() {
    var file = _CreateValidFile(backgroundColor: 0x05);

    var bytes = InterlaceStudioWriter.ToBytes(file);

    Assert.That(bytes[19002], Is.EqualTo(0x05));
  }

  private static InterlaceStudioFile _CreateValidFile(ushort loadAddress = 0x4000, byte backgroundColor = 0) => new() {
    LoadAddress = loadAddress,
    Bitmap1 = new byte[InterlaceStudioFile.BitmapDataSize],
    Screen1 = new byte[InterlaceStudioFile.ScreenDataSize],
    ColorData = new byte[InterlaceStudioFile.ColorDataSize],
    Bitmap2 = new byte[InterlaceStudioFile.BitmapDataSize],
    Screen2 = new byte[InterlaceStudioFile.ScreenDataSize],
    BackgroundColor = backgroundColor,
  };
}
