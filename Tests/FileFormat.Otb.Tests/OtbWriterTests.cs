using System;
using FileFormat.Otb;

namespace FileFormat.Otb.Tests;

[TestFixture]
public sealed class OtbWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderBytes() {
    var file = new OtbFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8] // 2 bytes per row * 4 rows
    };

    var bytes = OtbWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00), "InfoField");
    Assert.That(bytes[1], Is.EqualTo(16), "Width");
    Assert.That(bytes[2], Is.EqualTo(4), "Height");
    Assert.That(bytes[3], Is.EqualTo(0x01), "Depth");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelSize() {
    // 16x4: bytesPerRow=2, pixelData=8 bytes, total=4+8=12
    var file = new OtbFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8]
    };

    var bytes = OtbWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => OtbWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0b10110100, 0b01001011 };
    var file = new OtbFile {
      Width = 8,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = OtbWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(0b10110100));
    Assert.That(bytes[5], Is.EqualTo(0b01001011));
  }
}
