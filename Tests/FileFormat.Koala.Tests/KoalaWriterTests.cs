using System;
using FileFormat.Koala;

namespace FileFormat.Koala.Tests;

[TestFixture]
public sealed class KoalaWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KoalaWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly10003Bytes() {
    var file = _BuildValidKoalaFile();
    var bytes = KoalaWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(KoalaFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_WrittenAsLittleEndian() {
    var file = new KoalaFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0
    };

    var bytes = KoalaWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x60));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapDataOffset_StartsAtByte2() {
    var file = new KoalaFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0
    };
    file.BitmapData[0] = 0xAA;
    file.BitmapData[7999] = 0xBB;

    var bytes = KoalaWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[8001], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_VideoMatrixOffset_StartsAtByte8002() {
    var file = new KoalaFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0
    };
    file.VideoMatrix[0] = 0xCC;
    file.VideoMatrix[999] = 0xDD;

    var bytes = KoalaWriter.ToBytes(file);

    Assert.That(bytes[8002], Is.EqualTo(0xCC));
    Assert.That(bytes[9001], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ColorRamOffset_StartsAtByte9002() {
    var file = new KoalaFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0
    };
    file.ColorRam[0] = 0xEE;
    file.ColorRam[999] = 0xFF;

    var bytes = KoalaWriter.ToBytes(file);

    Assert.That(bytes[9002], Is.EqualTo(0xEE));
    Assert.That(bytes[10001], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BackgroundColor_AtLastByte() {
    var file = new KoalaFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 14
    };

    var bytes = KoalaWriter.ToBytes(file);

    Assert.That(bytes[10002], Is.EqualTo(14));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CustomLoadAddress_WrittenCorrectly() {
    var file = new KoalaFile {
      LoadAddress = 0x4000,
      BitmapData = new byte[8000],
      VideoMatrix = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0
    };

    var bytes = KoalaWriter.ToBytes(file);

    var loadAddress = (ushort)(bytes[0] | (bytes[1] << 8));
    Assert.That(loadAddress, Is.EqualTo(0x4000));
  }

  private static KoalaFile _BuildValidKoalaFile() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i % 256);

    var videoMatrix = new byte[1000];
    for (var i = 0; i < videoMatrix.Length; ++i)
      videoMatrix[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i + 5) % 16);

    return new() {
      LoadAddress = 0x6000,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = 6
    };
  }
}
