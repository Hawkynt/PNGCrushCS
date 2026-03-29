using System;
using FileFormat.SamCoupe;

namespace FileFormat.SamCoupe.Tests;

[TestFixture]
public sealed class SamCoupeWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Size24576() {
    var file = new SamCoupeFile {
      Width = 256,
      Height = 192,
      Mode = SamCoupeMode.Mode4,
      PixelData = new byte[192 * 128]
    };

    var bytes = SamCoupeWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(24576));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => SamCoupeWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPresent() {
    var pixelData = new byte[192 * 128];
    pixelData[0] = 0xAB;
    pixelData[128] = 0xCD; // row 1

    var file = new SamCoupeFile {
      Width = 256,
      Height = 192,
      Mode = SamCoupeMode.Mode4,
      PixelData = pixelData
    };

    var bytes = SamCoupeWriter.ToBytes(file);

    // Row 0 (even) goes to page 0 at offset 0
    Assert.That(bytes[0], Is.EqualTo(0xAB));
    // Row 1 (odd) goes to page 1 at offset 12288
    Assert.That(bytes[12288], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EvenLines_InFirstPage() {
    var pixelData = new byte[192 * 128];
    // Write recognizable bytes at row 2 (even, second even line)
    pixelData[2 * 128] = 0xEE;

    var file = new SamCoupeFile {
      Width = 256,
      Height = 192,
      Mode = SamCoupeMode.Mode4,
      PixelData = pixelData
    };

    var bytes = SamCoupeWriter.ToBytes(file);

    // Row 2 is the second even line (index 1 in page 0): offset = 1 * 128 = 128
    Assert.That(bytes[128], Is.EqualTo(0xEE));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OddLines_InSecondPage() {
    var pixelData = new byte[192 * 128];
    // Write recognizable byte at row 3 (odd, second odd line)
    pixelData[3 * 128] = 0xDD;

    var file = new SamCoupeFile {
      Width = 256,
      Height = 192,
      Mode = SamCoupeMode.Mode4,
      PixelData = pixelData
    };

    var bytes = SamCoupeWriter.ToBytes(file);

    // Row 3 is the second odd line (index 1 in page 1): offset = 12288 + 1 * 128 = 12416
    Assert.That(bytes[12416], Is.EqualTo(0xDD));
  }
}
