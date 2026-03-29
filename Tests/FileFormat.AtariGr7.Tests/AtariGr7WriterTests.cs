using System;
using FileFormat.AtariGr7;

namespace FileFormat.AtariGr7.Tests;

[TestFixture]
public sealed class AtariGr7WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces3840Bytes() {
    var file = new AtariGr7File {
      PixelData = new byte[160 * 96]
    };

    var bytes = AtariGr7Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(3840));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr7Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataEncoded() {
    var pixels = new byte[160 * 96];
    pixels[0] = 3;
    pixels[1] = 2;
    pixels[2] = 1;
    pixels[3] = 0;

    var file = new AtariGr7File { PixelData = pixels };

    var bytes = AtariGr7Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0b11100100));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllZeroPixels_ProducesZeroBytes() {
    var file = new AtariGr7File {
      PixelData = new byte[160 * 96]
    };

    var bytes = AtariGr7Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[3839], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllMaxPixels_ProducesFFBytes() {
    var pixels = new byte[160 * 96];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 3;

    var file = new AtariGr7File { PixelData = pixels };

    var bytes = AtariGr7Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MasksTo2Bits() {
    var pixels = new byte[160 * 96];
    pixels[0] = 0xFF;
    pixels[1] = 0xFE;
    pixels[2] = 0xFD;
    pixels[3] = 0xFC;

    var file = new AtariGr7File { PixelData = pixels };

    var bytes = AtariGr7Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0b11100100));
  }
}
