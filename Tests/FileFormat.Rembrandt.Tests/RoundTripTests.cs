using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Rembrandt;

namespace FileFormat.Rembrandt.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificPixelValues() {
    var pixelData = new byte[320 * 240 * 2];
    // First pixel: pure red in RGB565 BE = 0xF800
    pixelData[0] = 0xF8;
    pixelData[1] = 0x00;
    // Second pixel: pure green in RGB565 BE = 0x07E0
    pixelData[2] = 0x07;
    pixelData[3] = 0xE0;
    // Last pixel: pure blue in RGB565 BE = 0x001F
    pixelData[pixelData.Length - 2] = 0x00;
    pixelData[pixelData.Length - 1] = 0x1F;

    var original = new RembrandtFile {
      Width = 320,
      Height = 240,
      PixelData = pixelData,
    };

    var bytes = RembrandtWriter.ToBytes(original);
    var restored = RembrandtReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_640x480() {
    var pixelData = new byte[640 * 480 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new RembrandtFile {
      Width = 640,
      Height = 480,
      PixelData = pixelData,
    };

    var bytes = RembrandtWriter.ToBytes(original);
    var restored = RembrandtReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(640));
    Assert.That(restored.Height, Is.EqualTo(480));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new RembrandtFile {
      Width = 100,
      Height = 50,
      PixelData = new byte[100 * 50 * 2],
    };

    var bytes = RembrandtWriter.ToBytes(original);
    var restored = RembrandtReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(100));
    Assert.That(restored.Height, Is.EqualTo(50));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[200 * 100 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new RembrandtFile {
      Width = 200,
      Height = 100,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tcp");
    try {
      var bytes = RembrandtWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = RembrandtReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureRed() {
    var rgb24 = new byte[100 * 50 * 3];
    rgb24[0] = 255;
    rgb24[1] = 0;
    rgb24[2] = 0;

    var raw = new RawImage {
      Width = 100,
      Height = 50,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = RembrandtFile.FromRawImage(raw);
    var rawBack = RembrandtFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(255));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureGreen() {
    var rgb24 = new byte[100 * 50 * 3];
    rgb24[0] = 0;
    rgb24[1] = 255;
    rgb24[2] = 0;

    var raw = new RawImage {
      Width = 100,
      Height = 50,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = RembrandtFile.FromRawImage(raw);
    var rawBack = RembrandtFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(255));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureBlue() {
    var rgb24 = new byte[100 * 50 * 3];
    rgb24[0] = 0;
    rgb24[1] = 0;
    rgb24[2] = 255;

    var raw = new RawImage {
      Width = 100,
      Height = 50,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = RembrandtFile.FromRawImage(raw);
    var rawBack = RembrandtFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(255));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb565Precision() {
    var rgb24 = new byte[100 * 50 * 3];
    rgb24[0] = 200;
    rgb24[1] = 100;
    rgb24[2] = 50;

    var raw = new RawImage {
      Width = 100,
      Height = 50,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = RembrandtFile.FromRawImage(raw);
    var rawBack = RembrandtFile.ToRawImage(file);

    // R: 200>>3=25, (25<<3)|(25>>2)=200|6=206
    Assert.That(rawBack.PixelData[0], Is.EqualTo(206));
    // G: 100>>2=25, (25<<2)|(25>>4)=100|1=101
    Assert.That(rawBack.PixelData[1], Is.EqualTo(101));
    // B: 50>>3=6, (6<<3)|(6>>2)=48|1=49
    Assert.That(rawBack.PixelData[2], Is.EqualTo(49));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gradient() {
    var rgb24 = new byte[100 * 50 * 3];
    for (var i = 0; i < 100 * 50; ++i) {
      rgb24[i * 3] = (byte)(i % 256);
      rgb24[i * 3 + 1] = (byte)((i * 2) % 256);
      rgb24[i * 3 + 2] = (byte)((i * 3) % 256);
    }

    var raw = new RawImage {
      Width = 100,
      Height = 50,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = RembrandtFile.FromRawImage(raw);
    var rawBack = RembrandtFile.ToRawImage(file);

    Assert.That(rawBack.Width, Is.EqualTo(100));
    Assert.That(rawBack.Height, Is.EqualTo(50));
    Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(rawBack.PixelData.Length, Is.EqualTo(100 * 50 * 3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage_1x1() {
    var pixelData = new byte[2];
    pixelData[0] = 0xFF;
    pixelData[1] = 0xFF;

    var original = new RembrandtFile {
      Width = 1,
      Height = 1,
      PixelData = pixelData,
    };

    var bytes = RembrandtWriter.ToBytes(original);
    var restored = RembrandtReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var pixelData = new byte[100 * 50 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new RembrandtFile {
      Width = 100,
      Height = 50,
      PixelData = pixelData,
    };

    var bytes = RembrandtWriter.ToBytes(original);
    var restored = RembrandtReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
