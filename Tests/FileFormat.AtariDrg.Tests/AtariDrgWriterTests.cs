using System;
using FileFormat.AtariDrg;

namespace FileFormat.AtariDrg.Tests;

[TestFixture]
public sealed class AtariDrgWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces7680Bytes() {
    var file = new AtariDrgFile {
      PixelData = new byte[160 * 192]
    };

    var bytes = AtariDrgWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(7680));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariDrgWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataEncoded() {
    var pixels = new byte[160 * 192];
    pixels[0] = 3;
    pixels[1] = 2;
    pixels[2] = 1;
    pixels[3] = 0;

    var file = new AtariDrgFile { PixelData = pixels };

    var bytes = AtariDrgWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0b11100100));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllZeroPixels_ProducesZeroBytes() {
    var file = new AtariDrgFile {
      PixelData = new byte[160 * 192]
    };

    var bytes = AtariDrgWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[7679], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AllMaxPixels_ProducesFFBytes() {
    var pixels = new byte[160 * 192];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 3;

    var file = new AtariDrgFile { PixelData = pixels };

    var bytes = AtariDrgWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MasksTo2Bits() {
    var pixels = new byte[160 * 192];
    pixels[0] = 0xFF;
    pixels[1] = 0xFE;
    pixels[2] = 0xFD;
    pixels[3] = 0xFC;

    var file = new AtariDrgFile { PixelData = pixels };

    var bytes = AtariDrgWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0b11100100));
  }
}
