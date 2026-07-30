using System;
using System.IO;
using FileFormat.Core;
using FileFormat.LogoSys;

namespace FileFormat.LogoSys.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new LogoSysFile {
      Palette = new byte[768],
      PixelData = new byte[128000],
    };

    var bytes = LogoSysWriter.ToBytes(original);
    var restored = LogoSysReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(400));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PaletteAndPixelDataPreserved() {
    var palette = new byte[768];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i % 256);

    var pixelData = new byte[128000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new LogoSysFile {
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = LogoSysWriter.ToBytes(original);
    var restored = LogoSysReader.FromBytes(bytes);

    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new byte[768];
    palette[0] = 0xFF;
    palette[3] = 0x80;

    var pixelData = new byte[128000];
    pixelData[0] = 42;
    pixelData[127999] = 200;

    var original = new LogoSysFile {
      Palette = palette,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sys");
    try {
      var bytes = LogoSysWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = LogoSysReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_ViaRawImage() {
    var palette = new byte[768];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i % 256);

    var pixelData = new byte[128000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new LogoSysFile {
      Palette = palette,
      PixelData = pixelData,
    };

    var raw = LogoSysFile.ToRawImage(original);
    var restored = LogoSysFile.FromRawImage(raw);

    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new LogoSysFile {
      Palette = new byte[768],
      PixelData = new byte[128000],
    };

    var raw = LogoSysFile.ToRawImage(original);
    var restored = LogoSysFile.FromRawImage(raw);

    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_GradientImage() {
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)i;
      palette[i * 3 + 2] = (byte)i;
    }

    var pixelData = new byte[128000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new LogoSysFile {
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = LogoSysWriter.ToBytes(original);
    var restored = LogoSysReader.FromBytes(bytes);

    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
