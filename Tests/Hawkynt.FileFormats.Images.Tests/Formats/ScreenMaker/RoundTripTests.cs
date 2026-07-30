using System;
using System.IO;
using FileFormat.ScreenMaker;

namespace FileFormat.ScreenMaker.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriteRead_AllFieldsPreserved() {
    var palette = new byte[768];
    for (var i = 0; i < 768; ++i)
      palette[i] = (byte)(i % 256);

    var pixelData = new byte[256];
    for (var i = 0; i < 256; ++i)
      pixelData[i] = (byte)i;

    var original = new ScreenMakerFile {
      Width = 16,
      Height = 16,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = ScreenMakerWriter.ToBytes(original);
    var restored = ScreenMakerReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new ScreenMakerFile {
      Width = 8,
      Height = 4,
      Palette = new byte[768],
      PixelData = new byte[32],
    };

    var bytes = ScreenMakerWriter.ToBytes(original);
    var restored = ScreenMakerReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new byte[768];
    palette[0] = 255;
    palette[1] = 128;
    palette[2] = 64;

    var pixelData = new byte[64];
    for (var i = 0; i < 64; ++i)
      pixelData[i] = (byte)(i % 3);

    var original = new ScreenMakerFile {
      Width = 8,
      Height = 8,
      Palette = palette,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".smk");
    try {
      var bytes = ScreenMakerWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = ScreenMakerReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var palette = new byte[768];
    for (var i = 0; i < 768; ++i)
      palette[i] = (byte)(i * 7 % 256);

    var pixelData = new byte[320 * 200];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new ScreenMakerFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = ScreenMakerWriter.ToBytes(original);
    var restored = ScreenMakerReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream() {
    var original = new ScreenMakerFile {
      Width = 4,
      Height = 4,
      Palette = new byte[768],
      PixelData = new byte[16],
    };
    original.PixelData[0] = 0xFE;

    var bytes = ScreenMakerWriter.ToBytes(original);

    using var ms = new MemoryStream(bytes);
    var restored = ScreenMakerReader.FromStream(ms);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData[0], Is.EqualTo(0xFE));
  }
}
