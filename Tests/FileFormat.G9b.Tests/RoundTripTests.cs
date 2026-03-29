using System;
using System.IO;
using FileFormat.G9b;

namespace FileFormat.G9b.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode3_SmallImage() {
    var pixels = new byte[4 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 256);

    var original = new G9bFile {
      Width = 4,
      Height = 4,
      ScreenMode = G9bScreenMode.Indexed8,
      ColorMode = 0,
      PixelData = pixels,
    };

    var bytes = G9bWriter.ToBytes(original);
    var restored = G9bReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ScreenMode, Is.EqualTo(original.ScreenMode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode5_SmallImage() {
    var pixels = new byte[4 * 4 * 2];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 256);

    var original = new G9bFile {
      Width = 4,
      Height = 4,
      ScreenMode = G9bScreenMode.Rgb555,
      ColorMode = 0,
      PixelData = pixels,
    };

    var bytes = G9bWriter.ToBytes(original);
    var restored = G9bReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ScreenMode, Is.EqualTo(original.ScreenMode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode3_LargerImage() {
    var pixels = new byte[256 * 212];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 256);

    var original = new G9bFile {
      Width = 256,
      Height = 212,
      ScreenMode = G9bScreenMode.Indexed8,
      ColorMode = 0,
      PixelData = pixels,
    };

    var bytes = G9bWriter.ToBytes(original);
    var restored = G9bReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(212));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[8 * 8];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 256);

    var original = new G9bFile {
      Width = 8,
      Height = 8,
      ScreenMode = G9bScreenMode.Indexed8,
      ColorMode = 0,
      PixelData = pixels,
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".g9b");
    try {
      var bytes = G9bWriter.ToBytes(original);
      File.WriteAllBytes(tmpPath, bytes);

      var restored = G9bReader.FromFile(new FileInfo(tmpPath));
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Mode3() {
    var pixels = new byte[4 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 16);

    var original = new G9bFile {
      Width = 4,
      Height = 4,
      ScreenMode = G9bScreenMode.Indexed8,
      PixelData = pixels,
    };

    var raw = G9bFile.ToRawImage(original);
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));

    var restored = G9bFile.FromRawImage(raw);
    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ScreenMode, Is.EqualTo(G9bScreenMode.Indexed8));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ColorModePreserved() {
    var original = new G9bFile {
      Width = 2,
      Height = 2,
      ScreenMode = G9bScreenMode.Indexed8,
      ColorMode = 42,
      PixelData = new byte[4],
    };

    var bytes = G9bWriter.ToBytes(original);
    var restored = G9bReader.FromBytes(bytes);

    Assert.That(restored.ColorMode, Is.EqualTo(42));
  }
}
