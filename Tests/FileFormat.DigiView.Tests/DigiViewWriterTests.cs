using System;
using FileFormat.DigiView;

namespace FileFormat.DigiView.Tests;

[TestFixture]
public sealed class DigiViewWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DigiViewWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WidthBE_FirstTwoBytes() {
    var file = new DigiViewFile {
      Width = 0x0130, // 304
      Height = 1,
      Channels = 1,
      PixelData = new byte[304],
    };

    var bytes = DigiViewWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x01)); // high byte
    Assert.That(bytes[1], Is.EqualTo(0x30)); // low byte
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeightBE_Bytes2And3() {
    var file = new DigiViewFile {
      Width = 1,
      Height = 0x00C8, // 200
      Channels = 1,
      PixelData = new byte[200],
    };

    var bytes = DigiViewWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0x00)); // high byte
    Assert.That(bytes[3], Is.EqualTo(0xC8)); // low byte
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ChannelsByte_AtOffset4() {
    var file = new DigiViewFile {
      Width = 1,
      Height = 1,
      Channels = 3,
      PixelData = new byte[3],
    };

    var bytes = DigiViewWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_GrayscaleChannel_AtOffset4() {
    var file = new DigiViewFile {
      Width = 1,
      Height = 1,
      Channels = 1,
      PixelData = new byte[1],
    };

    var bytes = DigiViewWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPresent_AfterHeader() {
    var file = new DigiViewFile {
      Width = 2,
      Height = 2,
      Channels = 1,
      PixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD },
    };

    var bytes = DigiViewWriter.ToBytes(file);

    Assert.That(bytes[DigiViewFile.HeaderSize], Is.EqualTo(0xAA));
    Assert.That(bytes[DigiViewFile.HeaderSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[DigiViewFile.HeaderSize + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[DigiViewFile.HeaderSize + 3], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSizeCorrect_Grayscale() {
    var file = new DigiViewFile {
      Width = 16,
      Height = 16,
      Channels = 1,
      PixelData = new byte[256],
    };

    var bytes = DigiViewWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(DigiViewFile.HeaderSize + 256));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSizeCorrect_Rgb() {
    var file = new DigiViewFile {
      Width = 8,
      Height = 8,
      Channels = 3,
      PixelData = new byte[192],
    };

    var bytes = DigiViewWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(DigiViewFile.HeaderSize + 192));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BigEndian_Width256() {
    var file = new DigiViewFile {
      Width = 256,
      Height = 1,
      Channels = 1,
      PixelData = new byte[256],
    };

    var bytes = DigiViewWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x01)); // 256 >> 8
    Assert.That(bytes[1], Is.EqualTo(0x00)); // 256 & 0xFF
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BigEndian_Height512() {
    var file = new DigiViewFile {
      Width = 1,
      Height = 512,
      Channels = 1,
      PixelData = new byte[512],
    };

    var bytes = DigiViewWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0x02)); // 512 >> 8
    Assert.That(bytes[3], Is.EqualTo(0x00)); // 512 & 0xFF
  }
}
