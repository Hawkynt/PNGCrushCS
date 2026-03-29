using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffRgb8;

namespace FileFormat.IffRgb8.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_UncompressedRgb() {
    var pixelData = new byte[4 * 3 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new IffRgb8File {
      Width = 4,
      Height = 3,
      Compression = IffRgb8Compression.None,
      PixelData = pixelData,
    };

    var bytes = IffRgb8Writer.ToBytes(original);
    var restored = IffRgb8Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Compression, Is.EqualTo(IffRgb8Compression.None));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ByteRun1Compressed() {
    var pixelData = new byte[4 * 3 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new IffRgb8File {
      Width = 4,
      Height = 3,
      Compression = IffRgb8Compression.ByteRun1,
      PixelData = pixelData,
    };

    var bytes = IffRgb8Writer.ToBytes(original);
    var restored = IffRgb8Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Compression, Is.EqualTo(IffRgb8Compression.ByteRun1));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new IffRgb8File {
      Width = 8,
      Height = 4,
      Compression = IffRgb8Compression.ByteRun1,
      PixelData = new byte[8 * 4 * 3],
    };

    var bytes = IffRgb8Writer.ToBytes(original);
    var restored = IffRgb8Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(8));
      Assert.That(restored.Height, Is.EqualTo(4));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[3 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new IffRgb8File {
      Width = 3,
      Height = 2,
      Compression = IffRgb8Compression.ByteRun1,
      PixelData = pixelData,
    };

    var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rgb8");
    try {
      var bytes = IffRgb8Writer.ToBytes(original);
      File.WriteAllBytes(tempFile, bytes);
      var restored = IffRgb8Reader.FromFile(new FileInfo(tempFile));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(original.Width));
        Assert.That(restored.Height, Is.EqualTo(original.Height));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[2 * 2 * 3];
    pixelData[0] = 0xFF; pixelData[1] = 0x00; pixelData[2] = 0x00;
    pixelData[3] = 0x00; pixelData[4] = 0xFF; pixelData[5] = 0x00;
    pixelData[6] = 0x00; pixelData[7] = 0x00; pixelData[8] = 0xFF;
    pixelData[9] = 0x80; pixelData[10] = 0x80; pixelData[11] = 0x80;

    var rawImage = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = pixelData,
    };

    var file = IffRgb8File.FromRawImage(rawImage);
    var bytes = IffRgb8Writer.ToBytes(file);
    var restored = IffRgb8Reader.FromBytes(bytes);
    var restoredRaw = IffRgb8File.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(restoredRaw.Width, Is.EqualTo(2));
      Assert.That(restoredRaw.Height, Is.EqualTo(2));
      Assert.That(restoredRaw.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(restoredRaw.PixelData, Is.EqualTo(pixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 48;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new IffRgb8File {
      Width = width,
      Height = height,
      Compression = IffRgb8Compression.ByteRun1,
      PixelData = pixelData,
    };

    var bytes = IffRgb8Writer.ToBytes(original);
    var restored = IffRgb8Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new IffRgb8File {
      Width = 1,
      Height = 1,
      Compression = IffRgb8Compression.None,
      PixelData = [0xAA, 0xBB, 0xCC],
    };

    var bytes = IffRgb8Writer.ToBytes(original);
    var restored = IffRgb8Reader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(1));
      Assert.That(restored.Height, Is.EqualTo(1));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }
}
