using System;
using System.IO;
using FileFormat.Jbig;
using FileFormat.Core;

namespace FileFormat.Jbig.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllWhite() {
    var width = 32;
    var height = 8;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height]; // all zeros = all white

    var original = new JbigFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = JbigWriter.ToBytes(original);
    var restored = JbigReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBlack() {
    var width = 32;
    var height = 8;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    Array.Fill(pixelData, (byte)0xFF);

    var original = new JbigFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = JbigWriter.ToBytes(original);
    var restored = JbigReader.FromBytes(bytes);

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
        pixelData[row * bytesPerRow + col] = (byte)(row % 2 == 0 ? 0xAA : 0x55);

    var original = new JbigFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = JbigWriter.ToBytes(original);
    var restored = JbigReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Diagonal() {
    var width = 16;
    var height = 16;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    // Draw a diagonal line
    for (var i = 0; i < Math.Min(width, height); ++i)
      pixelData[i * bytesPerRow + (i >> 3)] |= (byte)(0x80 >> (i & 7));

    var original = new JbigFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = JbigWriter.ToBytes(original);
    var restored = JbigReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 24;
    var height = 6;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    pixelData[0] = 0xFF;
    pixelData[3] = 0xAA;
    pixelData[6] = 0x55;

    var original = new JbigFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jbg");
    try {
      var bytes = JbigWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = JbigReader.FromFile(new FileInfo(tempPath));

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
    var pixelData = new byte[] { 0b11001100, 0b00110011 };

    var original = new JbigFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var raw = JbigFile.ToRawImage(original);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.Width, Is.EqualTo(width));
    Assert.That(raw.Height, Is.EqualTo(height));

    var fromRaw = JbigFile.FromRawImage(raw);

    Assert.That(fromRaw.Width, Is.EqualTo(width));
    Assert.That(fromRaw.Height, Is.EqualTo(height));
    Assert.That(fromRaw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 128;
    var height = 64;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    // Create a pattern
    for (var row = 0; row < height; ++row)
      for (var col = 0; col < bytesPerRow; ++col)
        pixelData[row * bytesPerRow + col] = (byte)((row + col) % 256);

    var original = new JbigFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = JbigWriter.ToBytes(original);
    var restored = JbigReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleRow() {
    var original = new JbigFile {
      Width = 8,
      Height = 1,
      PixelData = [0b10101010]
    };

    var bytes = JbigWriter.ToBytes(original);
    var restored = JbigReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonByteAlignedWidth() {
    var width = 5;
    var height = 3;
    var bytesPerRow = (width + 7) / 8; // 1
    var pixelData = new byte[bytesPerRow * height];
    pixelData[0] = 0b11010000;
    pixelData[1] = 0b10100000;
    pixelData[2] = 0b01110000;

    var original = new JbigFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = JbigWriter.ToBytes(original);
    var restored = JbigReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    // Only compare the significant bits (width=5 means only 5 MSBs matter per row)
    for (var row = 0; row < height; ++row) {
      var mask = (byte)(0xFF << (8 - width));
      Assert.That((byte)(restored.PixelData[row] & mask), Is.EqualTo((byte)(original.PixelData[row] & mask)),
        $"Row {row} pixel data mismatch");
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_IdenticalRows_CompressesWithTPBON() {
    var width = 32;
    var height = 8;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    // All rows identical (all 0xFF)
    Array.Fill(pixelData, (byte)0xFF);

    var original = new JbigFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = JbigWriter.ToBytes(original);
    var restored = JbigReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    // TPBON should make this compress well - encoded data should be small
    Assert.That(bytes.Length, Is.LessThan(JbigHeader.StructSize + bytesPerRow * height));
  }
}
