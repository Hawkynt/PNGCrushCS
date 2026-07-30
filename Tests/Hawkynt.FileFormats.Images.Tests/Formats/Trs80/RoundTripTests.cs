using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Trs80;

namespace FileFormat.Trs80.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificCells() {
    var rawData = new byte[6144];
    // Cell 0: all 6 pixels set (bits 0-5)
    rawData[0] = 0x3F;
    // Cell 1: top-left only (bit 0)
    rawData[1] = 0x01;
    // Cell 2: bottom-right only (bit 5)
    rawData[2] = 0x20;
    // Last cell
    rawData[6143] = 0x15;

    var original = new Trs80File { RawData = rawData };

    var bytes = Trs80Writer.ToBytes(original);
    var restored = Trs80Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new Trs80File {
      RawData = new byte[6144]
    };

    var bytes = Trs80Writer.ToBytes(original);
    var restored = Trs80Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(144));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var rawData = new byte[6144];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new Trs80File { RawData = rawData };

    var bytes = Trs80Writer.ToBytes(original);
    var restored = Trs80Reader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[6144];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new Trs80File { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hr");
    try {
      var bytes = Trs80Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = Trs80Reader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var rawData = new byte[6144];
    // Set some specific cell patterns
    rawData[0] = 0x3F; // all 6 pixels on
    rawData[1] = 0x15; // checkerboard pattern (bits 0, 2, 4)
    rawData[128] = 0x2A; // inverse checkerboard (bits 1, 3, 5)

    var original = new Trs80File { RawData = rawData };

    var raw = Trs80File.ToRawImage(original);
    var restored = Trs80File.FromRawImage(raw);

    // Only the lower 6 bits matter (bits 6-7 are ignored in the format)
    for (var i = 0; i < 6144; ++i)
      Assert.That(restored.RawData[i] & 0x3F, Is.EqualTo(original.RawData[i] & 0x3F), $"Cell {i} mismatch");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new Trs80File { RawData = new byte[6144] };

    var raw = Trs80File.ToRawImage(original);
    var restored = Trs80File.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllPixelsSet() {
    var rawData = new byte[6144];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0x3F; // all 6 pixels set, bits 6-7 clear

    var original = new Trs80File { RawData = rawData };

    var raw = Trs80File.ToRawImage(original);
    var restored = Trs80File.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BitMapping_TopLeft() {
    var rawData = new byte[6144];
    rawData[0] = 0x01; // bit 0 = top-left pixel

    var file = new Trs80File { RawData = rawData };
    var raw = Trs80File.ToRawImage(file);

    // Pixel at (0,0) should be set (palette index 1)
    // In 1bpp MSB-first: byte 0, bit 7 = pixel (0,0)
    Assert.That((raw.PixelData[0] & 0x80) != 0, Is.True, "Top-left pixel should be set");

    // Pixel at (1,0) should be clear
    Assert.That((raw.PixelData[0] & 0x40) != 0, Is.False, "Top-right pixel should be clear");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BitMapping_TopRight() {
    var rawData = new byte[6144];
    rawData[0] = 0x02; // bit 1 = top-right pixel

    var file = new Trs80File { RawData = rawData };
    var raw = Trs80File.ToRawImage(file);

    // Pixel at (0,0) should be clear
    Assert.That((raw.PixelData[0] & 0x80) != 0, Is.False, "Top-left pixel should be clear");
    // Pixel at (1,0) should be set
    Assert.That((raw.PixelData[0] & 0x40) != 0, Is.True, "Top-right pixel should be set");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BitMapping_MidLeft() {
    var rawData = new byte[6144];
    rawData[0] = 0x04; // bit 2 = mid-left pixel

    var file = new Trs80File { RawData = rawData };
    var raw = Trs80File.ToRawImage(file);

    // Row 1 (y=1), pixel (0,1): byte index = 1 * 32 + 0/8 = 32, bit 7
    Assert.That((raw.PixelData[32] & 0x80) != 0, Is.True, "Mid-left pixel should be set");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BitMapping_BotRight() {
    var rawData = new byte[6144];
    rawData[0] = 0x20; // bit 5 = bot-right pixel

    var file = new Trs80File { RawData = rawData };
    var raw = Trs80File.ToRawImage(file);

    // Row 2 (y=2), pixel (1,2): byte index = 2 * 32 + 0, bit 6
    Assert.That((raw.PixelData[64] & 0x40) != 0, Is.True, "Bot-right pixel should be set");
  }
}
