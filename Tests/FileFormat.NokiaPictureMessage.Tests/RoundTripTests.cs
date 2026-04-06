using System;
using System.IO;
using FileFormat.NokiaPictureMessage;

namespace FileFormat.NokiaPictureMessage.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage() {
    var original = new NokiaPictureMessageFile {
      Width = 8,
      Height = 8,
      PixelData = new byte[] { 0xFF, 0x00, 0xAA, 0x55, 0xF0, 0x0F, 0xCC, 0x33 }
    };

    var bytes = NokiaPictureMessageWriter.ToBytes(original);
    var restored = NokiaPictureMessageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TypicalSize_72x28() {
    var width = 72;
    var height = 28;
    var bytesPerRow = (width + 7) / 8; // 9
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new NokiaPictureMessageFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = NokiaPictureMessageWriter.ToBytes(original);
    var restored = NokiaPictureMessageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonByteAlignedWidth() {
    // 7 pixels wide: bytesPerRow = ceil(7/8) = 1, 5 rows
    var original = new NokiaPictureMessageFile {
      Width = 7,
      Height = 5,
      PixelData = new byte[] { 0b11010000, 0b10100000, 0b01110000, 0b11111110, 0b00000010 }
    };

    var bytes = NokiaPictureMessageWriter.ToBytes(original);
    var restored = NokiaPictureMessageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(7));
    Assert.That(restored.Height, Is.EqualTo(5));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new NokiaPictureMessageFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0x80 }
    };

    var bytes = NokiaPictureMessageWriter.ToBytes(original);
    var restored = NokiaPictureMessageReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new NokiaPictureMessageFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE }
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".npm");
    try {
      var bytes = NokiaPictureMessageWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = NokiaPictureMessageReader.FromFile(new FileInfo(tempPath));

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
    var original = new NokiaPictureMessageFile {
      Width = 8,
      Height = 2,
      PixelData = new byte[] { 0xFF, 0xAA }
    };

    var raw = NokiaPictureMessageFile.ToRawImage(original);
    var restored = NokiaPictureMessageFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
