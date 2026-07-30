using System;
using System.IO;
using FileFormat.MasterSystemTile;

namespace FileFormat.MasterSystemTile.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleTile_AllValues() {
    var pixels = new byte[128 * 8];
    // Set first 16 pixels to values 0-15
    for (var i = 0; i < 8; ++i)
      pixels[i] = (byte)i;
    for (var i = 0; i < 8; ++i)
      pixels[128 + i] = (byte)(i + 8);

    var original = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = MasterSystemTileWriter.ToBytes(original);
    var restored = MasterSystemTileReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    for (var i = 0; i < 8; ++i)
      Assert.That(restored.PixelData[i], Is.EqualTo(i), $"Pixel {i} mismatch");
    for (var i = 0; i < 8; ++i)
      Assert.That(restored.PixelData[128 + i], Is.EqualTo(i + 8), $"Pixel {i + 8} mismatch");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleTiles() {
    var pixels = new byte[128 * 16];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 16);

    var original = new MasterSystemTileFile {
      Width = 128,
      Height = 16,
      PixelData = pixels
    };

    var bytes = MasterSystemTileWriter.ToBytes(original);
    var restored = MasterSystemTileReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var bytes = MasterSystemTileWriter.ToBytes(original);
    var restored = MasterSystemTileReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var pixels = new byte[128 * 8];
    Array.Fill(pixels, (byte)15);

    var original = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = MasterSystemTileWriter.ToBytes(original);
    var restored = MasterSystemTileReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[128 * 8];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 16);

    var original = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sms");
    try {
      var bytes = MasterSystemTileWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MasterSystemTileReader.FromFile(new FileInfo(tempPath));

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
    var pixels = new byte[128 * 8];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 16);

    var original = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var rawImage = MasterSystemTileFile.ToRawImage(original);
    var restored = MasterSystemTileFile.FromRawImage(rawImage);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SecondTile_PixelsPreserved() {
    var pixels = new byte[128 * 8];
    // Set pixels in tile 1 (columns 8-15)
    pixels[8] = 5;
    pixels[9] = 10;
    pixels[10] = 15;

    var original = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = MasterSystemTileWriter.ToBytes(original);
    var restored = MasterSystemTileReader.FromBytes(bytes);

    Assert.That(restored.PixelData[8], Is.EqualTo(5));
    Assert.That(restored.PixelData[9], Is.EqualTo(10));
    Assert.That(restored.PixelData[10], Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ByteLayout_RowInterleaved() {
    // Verify the interleave pattern: row 0 = bytes [0..3], row 1 = bytes [4..7], etc.
    var pixels = new byte[128 * 8];
    // Tile 0, pixel (0,0) = 5 (binary 0101): plane0=1, plane1=0, plane2=1, plane3=0
    pixels[0] = 5;

    var bytes = MasterSystemTileWriter.ToBytes(new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    });

    // Tile 0 row 0: bytes [0]=plane0, [1]=plane1, [2]=plane2, [3]=plane3
    Assert.That(bytes[0] & 0x80, Is.EqualTo(0x80), "Plane 0 MSB should be set (bit 0 of 5)");
    Assert.That(bytes[1] & 0x80, Is.EqualTo(0x00), "Plane 1 MSB should be clear (bit 1 of 5)");
    Assert.That(bytes[2] & 0x80, Is.EqualTo(0x80), "Plane 2 MSB should be set (bit 2 of 5)");
    Assert.That(bytes[3] & 0x80, Is.EqualTo(0x00), "Plane 3 MSB should be clear (bit 3 of 5)");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TotalByteSize_Correct() {
    var original = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var bytes = MasterSystemTileWriter.ToBytes(original);

    // 16 tiles across x 1 tile row x 32 bytes/tile = 512
    Assert.That(bytes.Length, Is.EqualTo(16 * 32));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ValuesMaskedTo4Bits() {
    // Pixel values > 15 should be masked to lower 4 bits
    var pixels = new byte[128 * 8];
    pixels[0] = 0xFF; // only lower 4 bits (15) should survive

    var original = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = MasterSystemTileWriter.ToBytes(original);
    var restored = MasterSystemTileReader.FromBytes(bytes);

    Assert.That(restored.PixelData[0], Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EachValueIndependently() {
    // Test each value 0-15 in isolation to verify all plane bits
    for (var v = 0; v < 16; ++v) {
      var pixels = new byte[128 * 8];
      pixels[0] = (byte)v;

      var file = new MasterSystemTileFile {
        Width = 128,
        Height = 8,
        PixelData = pixels
      };

      var bytes = MasterSystemTileWriter.ToBytes(file);
      var restored = MasterSystemTileReader.FromBytes(bytes);

      Assert.That(restored.PixelData[0], Is.EqualTo(v), $"Value {v} did not round-trip correctly");
    }
  }
}
