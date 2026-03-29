using System;
using FileFormat.Atari8Bit;

namespace FileFormat.Atari8Bit.Tests;

[TestFixture]
public sealed class Atari8BitWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => Atari8BitWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gr8_OutputSize7680() {
    var file = new Atari8BitFile {
      Width = 320,
      Height = 192,
      Mode = Atari8BitMode.Gr8,
      PixelData = new byte[320 * 192],
    };

    var bytes = Atari8BitWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(7680));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gr7_OutputSize1920() {
    var file = new Atari8BitFile {
      Width = 160,
      Height = 96,
      Mode = Atari8BitMode.Gr7,
      PixelData = new byte[160 * 96],
    };

    var bytes = Atari8BitWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(1920));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gr8_PixelDataEncoded() {
    var pixels = new byte[320 * 192];
    pixels[0] = 1;
    pixels[1] = 0;
    pixels[2] = 1;
    pixels[3] = 1;
    pixels[4] = 0;
    pixels[5] = 0;
    pixels[6] = 0;
    pixels[7] = 1;

    var file = new Atari8BitFile {
      Width = 320,
      Height = 192,
      Mode = Atari8BitMode.Gr8,
      PixelData = pixels,
    };

    var bytes = Atari8BitWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0b10110001));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gr15_PixelDataEncoded() {
    var pixels = new byte[160 * 192];
    pixels[0] = 3;
    pixels[1] = 2;
    pixels[2] = 1;
    pixels[3] = 0;

    var file = new Atari8BitFile {
      Width = 160,
      Height = 192,
      Mode = Atari8BitMode.Gr15,
      PixelData = pixels,
    };

    var bytes = Atari8BitWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0b11100100));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gr9_PixelDataEncoded() {
    var pixels = new byte[80 * 192];
    pixels[0] = 0x0A;
    pixels[1] = 0x05;

    var file = new Atari8BitFile {
      Width = 80,
      Height = 192,
      Mode = Atari8BitMode.Gr9,
      PixelData = pixels,
    };

    var bytes = Atari8BitWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xA5));
  }
}
