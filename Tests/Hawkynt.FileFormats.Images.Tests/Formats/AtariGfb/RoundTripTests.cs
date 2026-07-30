using System;
using System.IO;
using FileFormat.Core;
using FileFormat.AtariGfb;

namespace FileFormat.AtariGfb.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void AllZeros_RoundTrip() {
    var original = new AtariGfbFile { RawData = new byte[7680] };

    var bytes = AtariGfbWriter.ToBytes(original);
    var restored = AtariGfbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void AllOnes_RoundTrip() {
    var rawData = new byte[7680];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new AtariGfbFile { RawData = rawData };

    var bytes = AtariGfbWriter.ToBytes(original);
    var restored = AtariGfbReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void ViaFile_RoundTrip() {
    var rawData = new byte[7680];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new AtariGfbFile { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gfb");
    try {
      var bytes = AtariGfbWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AtariGfbReader.FromFile(new FileInfo(tempPath));

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
  public void ViaRawImage_RoundTrip() {
    var rawData = new byte[7680];
    rawData[0] = 0xAA;
    rawData[1] = 0x55;
    rawData[40] = 0xCC;

    var original = new AtariGfbFile { RawData = rawData };

    var raw = AtariGfbFile.ToRawImage(original);
    var restored = AtariGfbFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void ViaRawImage_AllZeros() {
    var original = new AtariGfbFile { RawData = new byte[7680] };

    var raw = AtariGfbFile.ToRawImage(original);
    var restored = AtariGfbFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void SpecificRow_PixelMapping_MsbFirst() {
    var rawData = new byte[7680];
    // Set byte 0 to 0x80 = MSB set = first pixel in row 0 is set
    rawData[0] = 0x80;

    var original = new AtariGfbFile { RawData = rawData };
    var raw = AtariGfbFile.ToRawImage(original);

    // In Indexed1 MSB-first, byte 0 bit 7 = pixel (0,0)
    Assert.That((raw.PixelData[0] & 0x80) != 0, Is.True, "Pixel (0,0) should be set for MSB 0x80");
    Assert.That((raw.PixelData[0] & 0x40) != 0, Is.False, "Pixel (1,0) should be clear");

    // Test byte 0 = 0x01 = LSB set = pixel 7 in that byte
    rawData[0] = 0x01;
    var original2 = new AtariGfbFile { RawData = rawData };
    var raw2 = AtariGfbFile.ToRawImage(original2);

    Assert.That((raw2.PixelData[0] & 0x01) != 0, Is.True, "Pixel (7,0) should be set for LSB 0x01");
    Assert.That((raw2.PixelData[0] & 0x80) != 0, Is.False, "Pixel (0,0) should be clear for LSB-only");
  }
}
