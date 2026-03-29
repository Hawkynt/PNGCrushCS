using System;
using FileFormat.AtariAgp;

namespace FileFormat.AtariAgp.Tests;

[TestFixture]
public sealed class AtariAgpWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariAgpWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gr8_Produces7680Bytes() {
    var file = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8,
      PixelData = new byte[320 * 192],
    };

    var bytes = AtariAgpWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(7680));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gr7_Produces3840Bytes() {
    var file = new AtariAgpFile {
      Width = 160,
      Height = 96,
      Mode = AtariAgpMode.Graphics7,
      PixelData = new byte[160 * 96],
    };

    var bytes = AtariAgpWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(3840));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gr8WithColors_Produces7682Bytes() {
    var file = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8WithColors,
      PixelData = new byte[320 * 192],
      ForegroundColor = 0x34,
      BackgroundColor = 0x12,
    };

    var bytes = AtariAgpWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(7682));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gr8WithColors_AppendedColorBytes() {
    var file = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8WithColors,
      PixelData = new byte[320 * 192],
      ForegroundColor = 0x34,
      BackgroundColor = 0x12,
    };

    var bytes = AtariAgpWriter.ToBytes(file);

    Assert.That(bytes[7680], Is.EqualTo(0x12));
    Assert.That(bytes[7681], Is.EqualTo(0x34));
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

    var file = new AtariAgpFile {
      Width = 320,
      Height = 192,
      Mode = AtariAgpMode.Graphics8,
      PixelData = pixels,
    };

    var bytes = AtariAgpWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0b10110001));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gr7_PixelDataEncoded() {
    var pixels = new byte[160 * 96];
    pixels[0] = 3;
    pixels[1] = 2;
    pixels[2] = 1;
    pixels[3] = 0;

    var file = new AtariAgpFile {
      Width = 160,
      Height = 96,
      Mode = AtariAgpMode.Graphics7,
      PixelData = pixels,
    };

    var bytes = AtariAgpWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0b11100100));
  }
}
