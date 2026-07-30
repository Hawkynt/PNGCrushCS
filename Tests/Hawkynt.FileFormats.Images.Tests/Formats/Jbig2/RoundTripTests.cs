using System;
using System.IO;
using FileFormat.Jbig2;
using FileFormat.Core;

namespace FileFormat.Jbig2.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllWhite() {
    var width = 32;
    var height = 4;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = Jbig2Writer.ToBytes(original);
    var restored = Jbig2Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBlack() {
    var width = 32;
    var height = 4;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    Array.Fill(pixelData, (byte)0xFF);

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = Jbig2Writer.ToBytes(original);
    var restored = Jbig2Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Checkerboard() {
    var width = 16;
    var height = 4;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    for (var row = 0; row < height; ++row)
      for (var col = 0; col < bytesPerRow; ++col)
        pixelData[row * bytesPerRow + col] = (row % 2 == 0) ? (byte)0b10101010 : (byte)0b01010101;

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = Jbig2Writer.ToBytes(original);
    var restored = Jbig2Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DiagonalLine() {
    var width = 16;
    var height = 16;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    for (var row = 0; row < height; ++row) {
      var col = row;
      if (col < width) {
        var byteIdx = row * bytesPerRow + (col >> 3);
        var bitIdx = 7 - (col & 7);
        pixelData[byteIdx] |= (byte)(1 << bitIdx);
      }
    }

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = Jbig2Writer.ToBytes(original);
    var restored = Jbig2Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 16;
    var height = 4;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    pixelData[0] = 0xFF;
    pixelData[3] = 0xFF;

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jb2");
    try {
      var bytes = Jbig2Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = Jbig2Reader.FromFile(new FileInfo(tempPath));

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
    var width = 8;
    var height = 2;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    pixelData[0] = 0b11110000;
    pixelData[1] = 0b00001111;

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var raw = Jbig2File.ToRawImage(original);
    var restored = Jbig2File.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 8;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    for (var row = 0; row < height; ++row)
      for (var byteIdx = 0; byteIdx < bytesPerRow; ++byteIdx)
        pixelData[row * bytesPerRow + byteIdx] = (byte)(byteIdx < row ? 0xFF : 0x00);

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = Jbig2Writer.ToBytes(original);
    var restored = Jbig2Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var width = 1;
    var height = 1;
    var pixelData = new byte[] { 0x80 }; // MSB = 1 (black pixel)

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = Jbig2Writer.ToBytes(original);
    var restored = Jbig2Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    // Only check the MSB (valid pixel)
    Assert.That((restored.PixelData[0] >> 7) & 1, Is.EqualTo(1));
  }
}
