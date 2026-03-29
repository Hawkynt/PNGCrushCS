using System;
using FileFormat.PlotMaker;

namespace FileFormat.PlotMaker.Tests;

[TestFixture]
public sealed class PlotMakerWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PlotMakerWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WidthLE_FirstTwoBytes() {
    var file = new PlotMakerFile {
      Width = 0x0130, // 304
      Height = 1,
      PixelData = new byte[(304 + 7) / 8],
    };

    var bytes = PlotMakerWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x30));
    Assert.That(bytes[1], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeightLE_Bytes2And3() {
    var file = new PlotMakerFile {
      Width = 8,
      Height = 0x00C8, // 200
      PixelData = new byte[1 * 200],
    };

    var bytes = PlotMakerWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xC8));
    Assert.That(bytes[3], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPresent_AfterHeader() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new PlotMakerFile {
      Width = 16,
      Height = 2,
      PixelData = pixelData,
    };

    var bytes = PlotMakerWriter.ToBytes(file);

    Assert.That(bytes[PlotMakerFile.HeaderSize], Is.EqualTo(0xAA));
    Assert.That(bytes[PlotMakerFile.HeaderSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[PlotMakerFile.HeaderSize + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[PlotMakerFile.HeaderSize + 3], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSizeCorrect_ByteAligned() {
    // width=16, height=4 -> bytesPerRow=2, total pixel=8
    var file = new PlotMakerFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8],
    };

    var bytes = PlotMakerWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(PlotMakerFile.HeaderSize + 8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSizeCorrect_NonByteAligned() {
    // width=10, height=3 -> bytesPerRow=ceil(10/8)=2, total pixel=6
    var file = new PlotMakerFile {
      Width = 10,
      Height = 3,
      PixelData = new byte[6],
    };

    var bytes = PlotMakerWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(PlotMakerFile.HeaderSize + 6));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SinglePixelWide() {
    // width=1, height=1 -> bytesPerRow=1, total pixel=1
    var file = new PlotMakerFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0x80 },
    };

    var bytes = PlotMakerWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(PlotMakerFile.HeaderSize + 1));
    Assert.That(bytes[PlotMakerFile.HeaderSize], Is.EqualTo(0x80));
  }
}
