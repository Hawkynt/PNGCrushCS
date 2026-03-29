using System;
using System.IO;
using FileFormat.SunIcon;

namespace FileFormat.SunIcon.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_16x16_DimensionsAndPixelDataPreserved() {
    var bytesPerRow = 2; // 16 / 8
    var pixelData = new byte[bytesPerRow * 16];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new SunIconFile {
      Width = 16,
      Height = 16,
      PixelData = pixelData
    };

    var bytes = SunIconWriter.ToBytes(original);
    var restored = SunIconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_32x32_DimensionsAndPixelDataPreserved() {
    var bytesPerRow = 4; // 32 / 8
    var pixelData = new byte[bytesPerRow * 32];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new SunIconFile {
      Width = 32,
      Height = 32,
      PixelData = pixelData
    };

    var bytes = SunIconWriter.ToBytes(original);
    var restored = SunIconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(32));
    Assert.That(restored.Height, Is.EqualTo(32));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_64x64_DimensionsAndPixelDataPreserved() {
    var bytesPerRow = 8; // 64 / 8
    var pixelData = new byte[bytesPerRow * 64];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new SunIconFile {
      Width = 64,
      Height = 64,
      PixelData = pixelData
    };

    var bytes = SunIconWriter.ToBytes(original);
    var restored = SunIconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(64));
    Assert.That(restored.Height, Is.EqualTo(64));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBlack() {
    var original = new SunIconFile {
      Width = 16,
      Height = 16,
      PixelData = new byte[32]
    };
    // all zeros = all white (background)
    Array.Fill(original.PixelData, (byte)0xFF); // all ones = all black (foreground)

    var bytes = SunIconWriter.ToBytes(original);
    var restored = SunIconReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllWhite() {
    var pixelData = new byte[32];
    // all zeros = all white (background)
    var original = new SunIconFile {
      Width = 16,
      Height = 16,
      PixelData = pixelData
    };

    var bytes = SunIconWriter.ToBytes(original);
    var restored = SunIconReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Checkerboard() {
    var pixelData = new byte[32];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);

    var original = new SunIconFile {
      Width = 16,
      Height = 16,
      PixelData = pixelData
    };

    var bytes = SunIconWriter.ToBytes(original);
    var restored = SunIconReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
    var original = new SunIconFile {
      Width = 16,
      Height = 2,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".icon");
    try {
      var bytes = SunIconWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = SunIconReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_OddByteCount_PadsCorrectly() {
    // 8 pixels wide = 1 byte per row, 3 rows = 3 bytes (odd)
    // Writer pads to 4 bytes (2 uint16 items), reader must extract 3 bytes
    var pixelData = new byte[] { 0xFF, 0xAA, 0x55 };
    var original = new SunIconFile {
      Width = 8,
      Height = 3,
      PixelData = pixelData
    };

    var bytes = SunIconWriter.ToBytes(original);
    var restored = SunIconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
