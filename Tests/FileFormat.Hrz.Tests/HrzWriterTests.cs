using System;
using FileFormat.Hrz;

namespace FileFormat.Hrz.Tests;

[TestFixture]
public sealed class HrzWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces184320Bytes() {
    var file = new HrzFile {
      PixelData = new byte[184320]
    };

    var bytes = HrzWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(184320));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[184320];
    pixelData[0] = 0xFF;
    pixelData[1] = 0x80;
    pixelData[2] = 0x40;
    pixelData[184319] = 0xDE;

    var file = new HrzFile {
      PixelData = pixelData
    };

    var bytes = HrzWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[1], Is.EqualTo(0x80));
    Assert.That(bytes[2], Is.EqualTo(0x40));
    Assert.That(bytes[184319], Is.EqualTo(0xDE));
  }
}
