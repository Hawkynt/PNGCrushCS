using System;
using FileFormat.AtariFalcon;

namespace FileFormat.AtariFalcon.Tests;

[TestFixture]
public sealed class AtariFalconWriterTests {

  private const int _EXPECTED_SIZE = 320 * 240 * 2;

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces153600Bytes() {
    var file = new AtariFalconFile {
      PixelData = new byte[_EXPECTED_SIZE]
    };

    var bytes = AtariFalconWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(_EXPECTED_SIZE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[_EXPECTED_SIZE];
    pixelData[0] = 0xFF;
    pixelData[1] = 0x80;
    pixelData[2] = 0x40;
    pixelData[_EXPECTED_SIZE - 1] = 0xDE;

    var file = new AtariFalconFile {
      PixelData = pixelData
    };

    var bytes = AtariFalconWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFF));
    Assert.That(bytes[1], Is.EqualTo(0x80));
    Assert.That(bytes[2], Is.EqualTo(0x40));
    Assert.That(bytes[_EXPECTED_SIZE - 1], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortData_PadsWithZeros() {
    var file = new AtariFalconFile {
      PixelData = new byte[10]
    };

    var bytes = AtariFalconWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(_EXPECTED_SIZE));
    Assert.That(bytes[10], Is.EqualTo(0));
    Assert.That(bytes[_EXPECTED_SIZE - 1], Is.EqualTo(0));
  }
}
