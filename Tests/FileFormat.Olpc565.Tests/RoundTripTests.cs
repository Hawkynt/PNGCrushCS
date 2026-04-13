using System;
using System.IO;
using FileFormat.Olpc565;

namespace FileFormat.Olpc565.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage() {
    var pixelData = new byte[] { 0x00, 0xF8, 0xE0, 0x07, 0x1F, 0x00, 0xFF, 0xFF };
    var original = new Olpc565File {
      Width = 2,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = Olpc565Writer.ToBytes(original);
    var restored = Olpc565Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new Olpc565File {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0x00, 0x00 } // black
    };

    var bytes = Olpc565Writer.ToBytes(original);
    var restored = Olpc565Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 320;
    var height = 240;
    var pixelData = new byte[width * height * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new Olpc565File {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = Olpc565Writer.ToBytes(original);
    var restored = Olpc565Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new Olpc565File {
      Width = 4,
      Height = 2,
      PixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 }
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".565");
    try {
      var bytes = Olpc565Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = Olpc565Reader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_ColorConversion_PureRed() {
    // Pure red in RGB565: R=31, G=0, B=0 => 0xF800
    var original = new Olpc565File {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0x00, 0xF8 }
    };

    var raw = Olpc565File.ToRawImage(original);

    Assert.That(raw.PixelData[0], Is.EqualTo(255)); // R
    Assert.That(raw.PixelData[1], Is.EqualTo(0));   // G
    Assert.That(raw.PixelData[2], Is.EqualTo(0));   // B
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ColorConversion_PureGreen() {
    // Pure green in RGB565: R=0, G=63, B=0 => 0x07E0
    var original = new Olpc565File {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0xE0, 0x07 }
    };

    var raw = Olpc565File.ToRawImage(original);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));   // R
    Assert.That(raw.PixelData[1], Is.EqualTo(255)); // G
    Assert.That(raw.PixelData[2], Is.EqualTo(0));   // B
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ColorConversion_PureBlue() {
    // Pure blue in RGB565: R=0, G=0, B=31 => 0x001F
    var original = new Olpc565File {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0x1F, 0x00 }
    };

    var raw = Olpc565File.ToRawImage(original);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));   // R
    Assert.That(raw.PixelData[1], Is.EqualTo(0));   // G
    Assert.That(raw.PixelData[2], Is.EqualTo(255)); // B
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var original = new Olpc565File {
      Width = 2,
      Height = 1,
      PixelData = new byte[] { 0xFF, 0xFF, 0x00, 0x00 } // white, black
    };

    var raw = Olpc565File.ToRawImage(original);
    var restored = Olpc565File.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
