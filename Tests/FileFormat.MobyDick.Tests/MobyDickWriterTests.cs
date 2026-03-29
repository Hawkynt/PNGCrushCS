using System;
using FileFormat.MobyDick;

namespace FileFormat.MobyDick.Tests;

[TestFixture]
public sealed class MobyDickWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MobyDickWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly64768Bytes() {
    var file = _BuildValidMobyDickFile();
    var bytes = MobyDickWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(MobyDickFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteAtOffset0() {
    var file = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };
    file.Palette[0] = 0xAA;
    file.Palette[767] = 0xBB;

    var bytes = MobyDickWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xAA));
    Assert.That(bytes[767], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataAtOffset768() {
    var file = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };
    file.PixelData[0] = 0xCC;
    file.PixelData[63999] = 0xDD;

    var bytes = MobyDickWriter.ToBytes(file);

    Assert.That(bytes[768], Is.EqualTo(0xCC));
    Assert.That(bytes[64767], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SectionsDoNotOverlap() {
    var file = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };

    Array.Fill(file.Palette, (byte)0x11);
    Array.Fill(file.PixelData, (byte)0x22);

    var bytes = MobyDickWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x11));
    Assert.That(bytes[767], Is.EqualTo(0x11));
    Assert.That(bytes[768], Is.EqualTo(0x22));
    Assert.That(bytes[64767], Is.EqualTo(0x22));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteRGBTriples_Preserved() {
    var file = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };

    // Set color index 0 to red (255, 0, 0)
    file.Palette[0] = 255;
    file.Palette[1] = 0;
    file.Palette[2] = 0;

    // Set color index 1 to green (0, 255, 0)
    file.Palette[3] = 0;
    file.Palette[4] = 255;
    file.Palette[5] = 0;

    // Set color index 255 to blue (0, 0, 255)
    file.Palette[765] = 0;
    file.Palette[766] = 0;
    file.Palette[767] = 255;

    var bytes = MobyDickWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(255));
    Assert.That(bytes[1], Is.EqualTo(0));
    Assert.That(bytes[2], Is.EqualTo(0));
    Assert.That(bytes[3], Is.EqualTo(0));
    Assert.That(bytes[4], Is.EqualTo(255));
    Assert.That(bytes[5], Is.EqualTo(0));
    Assert.That(bytes[765], Is.EqualTo(0));
    Assert.That(bytes[766], Is.EqualTo(0));
    Assert.That(bytes[767], Is.EqualTo(255));
  }

  private static MobyDickFile _BuildValidMobyDickFile() {
    var palette = new byte[768];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i % 256);

    var pixelData = new byte[64000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    return new() {
      Palette = palette,
      PixelData = pixelData
    };
  }
}
