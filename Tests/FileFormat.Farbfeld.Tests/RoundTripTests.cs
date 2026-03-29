using System;
using FileFormat.Farbfeld;

namespace FileFormat.Farbfeld.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1() {
    var pixelData = new byte[8];
    pixelData[0] = 0xFF; pixelData[1] = 0x00; // R = 0xFF00
    pixelData[2] = 0x80; pixelData[3] = 0x40; // G = 0x8040
    pixelData[4] = 0x00; pixelData[5] = 0xFF; // B = 0x00FF
    pixelData[6] = 0xFF; pixelData[7] = 0xFF; // A = 0xFFFF

    var original = new FarbfeldFile {
      Width = 1,
      Height = 1,
      PixelData = pixelData
    };

    var bytes = FarbfeldWriter.ToBytes(original);
    var restored = FarbfeldReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_4x3() {
    var pixelData = new byte[4 * 3 * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new FarbfeldFile {
      Width = 4,
      Height = 3,
      PixelData = pixelData
    };

    var bytes = FarbfeldWriter.ToBytes(original);
    var restored = FarbfeldReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(3));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    const int width = 100;
    const int height = 75;
    var pixelData = new byte[width * height * 8];
    var rng = new Random(42);
    rng.NextBytes(pixelData);

    var original = new FarbfeldFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = FarbfeldWriter.ToBytes(original);
    var restored = FarbfeldReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeroPixels() {
    var pixelData = new byte[2 * 2 * 8]; // all zeros

    var original = new FarbfeldFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = FarbfeldWriter.ToBytes(original);
    var restored = FarbfeldReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxPixels() {
    var pixelData = new byte[2 * 2 * 8];
    Array.Fill(pixelData, (byte)0xFF);

    var original = new FarbfeldFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = FarbfeldWriter.ToBytes(original);
    var restored = FarbfeldReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[3 * 2 * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new FarbfeldFile {
      Width = 3,
      Height = 2,
      PixelData = pixelData
    };

    var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".ff");
    try {
      var bytes = FarbfeldWriter.ToBytes(original);
      System.IO.File.WriteAllBytes(tempPath, bytes);

      var restored = FarbfeldReader.FromFile(new System.IO.FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(3));
        Assert.That(restored.Height, Is.EqualTo(2));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (System.IO.File.Exists(tempPath))
        System.IO.File.Delete(tempPath);
    }
  }
}
