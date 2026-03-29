using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffRgbn;

namespace FileFormat.IffRgbn.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage_PixelsQuantizedCorrectly() {
    // Values that are exact multiples of 17 survive quantization exactly
    var pixelData = new byte[] {
      0x00, 0x11, 0x22,
      0x33, 0x44, 0x55,
      0x66, 0x77, 0x88,
      0x99, 0xAA, 0xBB,
    };

    var original = new IffRgbnFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData,
    };

    var bytes = IffRgbnWriter.ToBytes(original);
    var restored = IffRgbnReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonMultipleOf17_Quantized() {
    // 0x80 = 128. (128 + 8) / 17 = 8. 8 * 17 = 136 = 0x88.
    var original = new IffRgbnFile {
      Width = 1,
      Height = 1,
      PixelData = [0x80, 0x80, 0x80],
    };

    var bytes = IffRgbnWriter.ToBytes(original);
    var restored = IffRgbnReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.PixelData[0], Is.EqualTo(0x88));
      Assert.That(restored.PixelData[1], Is.EqualTo(0x88));
      Assert.That(restored.PixelData[2], Is.EqualTo(0x88));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBlack() {
    var original = new IffRgbnFile {
      Width = 4,
      Height = 4,
      PixelData = new byte[4 * 4 * 3],
    };

    var bytes = IffRgbnWriter.ToBytes(original);
    var restored = IffRgbnReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(4));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllWhite() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new IffRgbnFile {
      Width = 4,
      Height = 4,
      PixelData = pixelData,
    };

    var bytes = IffRgbnWriter.ToBytes(original);
    var restored = IffRgbnReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(4));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[] { 0x00, 0x88, 0xFF, 0x11, 0x22, 0x33 };
    var original = new IffRgbnFile {
      Width = 2,
      Height = 1,
      PixelData = pixelData,
    };

    var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rgbn");
    try {
      var bytes = IffRgbnWriter.ToBytes(original);
      File.WriteAllBytes(tempFile, bytes);
      var restored = IffRgbnReader.FromFile(new FileInfo(tempFile));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(original.Width));
        Assert.That(restored.Height, Is.EqualTo(original.Height));
      });
    } finally {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    // Use quantization-safe values (multiples of 17)
    var pixelData = new byte[] { 0xFF, 0x00, 0x88, 0x11, 0xCC, 0x55 };
    var rawImage = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixelData,
    };

    var file = IffRgbnFile.FromRawImage(rawImage);
    var bytes = IffRgbnWriter.ToBytes(file);
    var restored = IffRgbnReader.FromBytes(bytes);
    var restoredRaw = IffRgbnFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(restoredRaw.Width, Is.EqualTo(2));
      Assert.That(restoredRaw.Height, Is.EqualTo(1));
      Assert.That(restoredRaw.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new IffRgbnFile {
      Width = 1,
      Height = 1,
      PixelData = [0xAA, 0x55, 0x00],
    };

    var bytes = IffRgbnWriter.ToBytes(original);
    var restored = IffRgbnReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(1));
      Assert.That(restored.Height, Is.EqualTo(1));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 32;
    var height = 24;
    // Use quantization-safe values (multiples of 17)
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)((i % 16) * 17);

    var original = new IffRgbnFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = IffRgbnWriter.ToBytes(original);
    var restored = IffRgbnReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    });
  }
}
