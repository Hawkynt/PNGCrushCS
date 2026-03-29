using System;
using FileFormat.NokiaPictureMessage;

namespace FileFormat.NokiaPictureMessage.Tests;

[TestFixture]
public sealed class NokiaPictureMessageWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => NokiaPictureMessageWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderBytes() {
    var file = new NokiaPictureMessageFile {
      Width = 72,
      Height = 28,
      PixelData = new byte[(72 + 7) / 8 * 28]
    };

    var bytes = NokiaPictureMessageWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00), "Type");
    Assert.That(bytes[1], Is.EqualTo(72), "Width");
    Assert.That(bytes[2], Is.EqualTo(28), "Height");
    Assert.That(bytes[3], Is.EqualTo(0x01), "Depth");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    // 8x2: bytesPerRow=1, 2 rows = 2 bytes pixel data, total = 4+2 = 6
    var file = new NokiaPictureMessageFile {
      Width = 8,
      Height = 2,
      PixelData = new byte[2]
    };

    var bytes = NokiaPictureMessageWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0b10110100, 0b01001011 };
    var file = new NokiaPictureMessageFile {
      Width = 8,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = NokiaPictureMessageWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(0b10110100));
    Assert.That(bytes[5], Is.EqualTo(0b01001011));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NonByteAlignedWidth() {
    // 5 pixels wide: bytesPerRow = ceil(5/8) = 1, 1 row
    // total = 4 + 1 = 5
    var file = new NokiaPictureMessageFile {
      Width = 5,
      Height = 1,
      PixelData = new byte[1]
    };

    var bytes = NokiaPictureMessageWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(5));
  }
}
