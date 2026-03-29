using System;
using System.IO;
using FileFormat.AtariFalcon;
using FileFormat.Core;

namespace FileFormat.AtariFalcon.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private const int _EXPECTED_SIZE = 320 * 240 * 2;

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificPixelValues() {
    var pixelData = new byte[_EXPECTED_SIZE];
    // First pixel: pure red in RGB565 BE = 0xF800
    pixelData[0] = 0xF8;
    pixelData[1] = 0x00;
    // Second pixel: pure green in RGB565 BE = 0x07E0
    pixelData[2] = 0x07;
    pixelData[3] = 0xE0;
    // Last pixel: pure blue in RGB565 BE = 0x001F
    pixelData[_EXPECTED_SIZE - 2] = 0x00;
    pixelData[_EXPECTED_SIZE - 1] = 0x1F;

    var original = new AtariFalconFile {
      PixelData = pixelData
    };

    var bytes = AtariFalconWriter.ToBytes(original);
    var restored = AtariFalconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AtariFalconFile {
      PixelData = new byte[_EXPECTED_SIZE]
    };

    var bytes = AtariFalconWriter.ToBytes(original);
    var restored = AtariFalconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(240));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[_EXPECTED_SIZE];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new AtariFalconFile {
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ftc");
    try {
      var bytes = AtariFalconWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AtariFalconReader.FromFile(new FileInfo(tempPath));

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
    // Create an image with pixel (0,0) = pure red
    var rgb24 = new byte[320 * 240 * 3];
    rgb24[0] = 255;
    rgb24[1] = 0;
    rgb24[2] = 0;

    var raw = new RawImage {
      Width = 320,
      Height = 240,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = AtariFalconFile.FromRawImage(raw);
    var rawBack = AtariFalconFile.ToRawImage(file);

    // RGB565 loses precision; red 255 => 5-bit 31 => expanded back to (31<<3)|(31>>2) = 255
    Assert.That(rawBack.PixelData[0], Is.EqualTo(255));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureGreen() {
    var rgb24 = new byte[320 * 240 * 3];
    rgb24[0] = 0;
    rgb24[1] = 255;
    rgb24[2] = 0;

    var raw = new RawImage {
      Width = 320,
      Height = 240,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = AtariFalconFile.FromRawImage(raw);
    var rawBack = AtariFalconFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(0));
    // Green 255 => 6-bit 63 => expanded back to (63<<2)|(63>>4) = 255
    Assert.That(rawBack.PixelData[1], Is.EqualTo(255));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureBlue() {
    var rgb24 = new byte[320 * 240 * 3];
    rgb24[0] = 0;
    rgb24[1] = 0;
    rgb24[2] = 255;

    var raw = new RawImage {
      Width = 320,
      Height = 240,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = AtariFalconFile.FromRawImage(raw);
    var rawBack = AtariFalconFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(255));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb565Precision() {
    // Verify RGB565 round-trip for a value that loses precision
    // R=200 => 200>>3 = 25 => (25<<3)|(25>>2) = 200+6 = 206
    var rgb24 = new byte[320 * 240 * 3];
    rgb24[0] = 200;
    rgb24[1] = 100;
    rgb24[2] = 50;

    var raw = new RawImage {
      Width = 320,
      Height = 240,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = AtariFalconFile.FromRawImage(raw);
    var rawBack = AtariFalconFile.ToRawImage(file);

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
    var rgb24 = new byte[320 * 240 * 3];
    for (var i = 0; i < 320 * 240; ++i) {
      rgb24[i * 3] = (byte)(i % 256);
      rgb24[i * 3 + 1] = (byte)((i * 2) % 256);
      rgb24[i * 3 + 2] = (byte)((i * 3) % 256);
    }

    var raw = new RawImage {
      Width = 320,
      Height = 240,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = AtariFalconFile.FromRawImage(raw);
    var rawBack = AtariFalconFile.ToRawImage(file);

    Assert.That(rawBack.Width, Is.EqualTo(320));
    Assert.That(rawBack.Height, Is.EqualTo(240));
    Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(rawBack.PixelData.Length, Is.EqualTo(320 * 240 * 3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var pixelData = new byte[_EXPECTED_SIZE];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new AtariFalconFile {
      PixelData = pixelData
    };

    var bytes = AtariFalconWriter.ToBytes(original);
    var restored = AtariFalconReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
