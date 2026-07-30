using System;
using System.IO;
using FileFormat.XvThumbnail;

namespace FileFormat.XvThumbnail.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage() {
    var original = new XvThumbnailFile {
      Width = 3,
      Height = 2,
      PixelData = [0xE0, 0x1C, 0x03, 0xFF, 0x00, 0x92],
    };

    var bytes = XvThumbnailWriter.ToBytes(original);
    var restored = XvThumbnailReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new XvThumbnailFile {
      Width = 4,
      Height = 4,
      PixelData = new byte[16],
    };

    var bytes = XvThumbnailWriter.ToBytes(original);
    var restored = XvThumbnailReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMax() {
    var pixels = new byte[9];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = 0xFF;

    var original = new XvThumbnailFile {
      Width = 3,
      Height = 3,
      PixelData = pixels,
    };

    var bytes = XvThumbnailWriter.ToBytes(original);
    var restored = XvThumbnailReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[20];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var original = new XvThumbnailFile {
      Width = 5,
      Height = 4,
      PixelData = pixels,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xv");
    try {
      var bytes = XvThumbnailWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = XvThumbnailReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_ViaRawImage() {
    var original = new XvThumbnailFile {
      Width = 2,
      Height = 2,
      PixelData = [0xE0, 0x1C, 0x03, 0xFF],
    };

    var raw = XvThumbnailFile.ToRawImage(original);
    var restored = XvThumbnailFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureRed() {
    // 0xE0 = 111_000_00 = R=7, G=0, B=0
    var original = new XvThumbnailFile {
      Width = 1,
      Height = 1,
      PixelData = [0xE0],
    };

    var raw = XvThumbnailFile.ToRawImage(original);

    Assert.That(raw.PixelData[0], Is.EqualTo(255)); // R
    Assert.That(raw.PixelData[1], Is.EqualTo(0));   // G
    Assert.That(raw.PixelData[2], Is.EqualTo(0));   // B

    var restored = XvThumbnailFile.FromRawImage(raw);
    Assert.That(restored.PixelData[0], Is.EqualTo(0xE0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureGreen() {
    // 0x1C = 000_111_00 = R=0, G=7, B=0
    var original = new XvThumbnailFile {
      Width = 1,
      Height = 1,
      PixelData = [0x1C],
    };

    var raw = XvThumbnailFile.ToRawImage(original);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));   // R
    Assert.That(raw.PixelData[1], Is.EqualTo(255)); // G
    Assert.That(raw.PixelData[2], Is.EqualTo(0));   // B

    var restored = XvThumbnailFile.FromRawImage(raw);
    Assert.That(restored.PixelData[0], Is.EqualTo(0x1C));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureBlue() {
    // 0x03 = 000_000_11 = R=0, G=0, B=3
    var original = new XvThumbnailFile {
      Width = 1,
      Height = 1,
      PixelData = [0x03],
    };

    var raw = XvThumbnailFile.ToRawImage(original);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));   // R
    Assert.That(raw.PixelData[1], Is.EqualTo(0));   // G
    Assert.That(raw.PixelData[2], Is.EqualTo(255)); // B

    var restored = XvThumbnailFile.FromRawImage(raw);
    Assert.That(restored.PixelData[0], Is.EqualTo(0x03));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Black() {
    var original = new XvThumbnailFile {
      Width = 1,
      Height = 1,
      PixelData = [0x00],
    };

    var raw = XvThumbnailFile.ToRawImage(original);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));
    Assert.That(raw.PixelData[1], Is.EqualTo(0));
    Assert.That(raw.PixelData[2], Is.EqualTo(0));

    var restored = XvThumbnailFile.FromRawImage(raw);
    Assert.That(restored.PixelData[0], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_White() {
    // 0xFF = 111_111_11 = R=7, G=7, B=3
    var original = new XvThumbnailFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF],
    };

    var raw = XvThumbnailFile.ToRawImage(original);

    Assert.That(raw.PixelData[0], Is.EqualTo(255));
    Assert.That(raw.PixelData[1], Is.EqualTo(255));
    Assert.That(raw.PixelData[2], Is.EqualTo(255));

    var restored = XvThumbnailFile.FromRawImage(raw);
    Assert.That(restored.PixelData[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllPacked_ValuesSurvive() {
    // Test all 256 possible packed values survive round-trip through RawImage
    var pixels = new byte[256];
    for (var i = 0; i < 256; ++i)
      pixels[i] = (byte)i;

    var original = new XvThumbnailFile {
      Width = 256,
      Height = 1,
      PixelData = pixels,
    };

    var raw = XvThumbnailFile.ToRawImage(original);
    var restored = XvThumbnailFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
