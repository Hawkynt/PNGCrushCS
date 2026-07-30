using System;
using System.IO;
using FileFormat.Core;
using FileFormat.AppleIIHgr;

namespace FileFormat.AppleIIHgr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AppleIIHgrFile {
      RawData = new byte[8192]
    };

    var bytes = AppleIIHgrWriter.ToBytes(original);
    var restored = AppleIIHgrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(280));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var rawData = new byte[8192];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new AppleIIHgrFile { RawData = rawData };

    var bytes = AppleIIHgrWriter.ToBytes(original);
    var restored = AppleIIHgrReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[8192];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new AppleIIHgrFile { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hgr");
    try {
      var bytes = AppleIIHgrWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AppleIIHgrReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new AppleIIHgrFile { RawData = new byte[8192] };

    var raw = AppleIIHgrFile.ToRawImage(original);
    var restored = AppleIIHgrFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PreservesDataBits() {
    var rawData = new byte[8192];
    // Set some bytes in row 0 (offset 0 for row 0)
    rawData[0] = 0x55; // bits 0,2,4,6 set (only bits 0-6 matter)
    rawData[1] = 0x2A; // bits 1,3,5 set
    // Set a byte in a different interleaved row
    rawData[1024] = 0x7F; // all 7 data bits set (row 1 starts at offset 1024)

    var original = new AppleIIHgrFile { RawData = rawData };

    var raw = AppleIIHgrFile.ToRawImage(original);
    var restored = AppleIIHgrFile.FromRawImage(raw);

    // Only the lower 7 bits matter (bit 7 is palette select, ignored for mono)
    for (var i = 0; i < 8192; ++i)
      Assert.That(restored.RawData[i] & 0x7F, Is.EqualTo(original.RawData[i] & 0x7F), $"Byte {i} mismatch");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllPixelsSet() {
    var rawData = new byte[8192];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0x7F; // all 7 data bits set, palette bit clear

    var original = new AppleIIHgrFile { RawData = rawData };

    var raw = AppleIIHgrFile.ToRawImage(original);
    var restored = AppleIIHgrFile.FromRawImage(raw);

    // Only check data bits in rows that actually map to valid offsets
    for (var y = 0; y < 192; ++y) {
      var offset = AppleIIHgrFile.GetRowOffset(y);
      for (var col = 0; col < 40; ++col)
        Assert.That(restored.RawData[offset + col] & 0x7F, Is.EqualTo(0x7F), $"Row {y}, col {col} mismatch");
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Interleave_Row0_OffsetIs0() {
    Assert.That(AppleIIHgrFile.GetRowOffset(0), Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Interleave_Row1_OffsetIs1024() {
    // Row 1: (1%8)*1024 + (1/64)*40 + ((1/8)%8)*128 = 1*1024 + 0 + 0 = 1024
    Assert.That(AppleIIHgrFile.GetRowOffset(1), Is.EqualTo(1024));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Interleave_Row8_OffsetIs128() {
    // Row 8: (8%8)*1024 + (8/64)*40 + ((8/8)%8)*128 = 0 + 0 + 1*128 = 128
    Assert.That(AppleIIHgrFile.GetRowOffset(8), Is.EqualTo(128));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Interleave_Row64_OffsetIs40() {
    // Row 64: (64%8)*1024 + (64/64)*40 + ((64/8)%8)*128 = 0 + 40 + 0 = 40
    Assert.That(AppleIIHgrFile.GetRowOffset(64), Is.EqualTo(40));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BitMapping_FirstPixelSet() {
    var rawData = new byte[8192];
    rawData[0] = 0x01; // bit 0 = first pixel in row 0

    var file = new AppleIIHgrFile { RawData = rawData };
    var raw = AppleIIHgrFile.ToRawImage(file);

    // In Indexed1 MSB-first: byte 0, bit 7 = pixel (0,0)
    Assert.That((raw.PixelData[0] & 0x80) != 0, Is.True, "First pixel should be set");
    Assert.That((raw.PixelData[0] & 0x40) != 0, Is.False, "Second pixel should be clear");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BitMapping_SeventhPixelSet() {
    var rawData = new byte[8192];
    rawData[0] = 0x40; // bit 6 = seventh pixel in byte 0

    var file = new AppleIIHgrFile { RawData = rawData };
    var raw = AppleIIHgrFile.ToRawImage(file);

    // Pixel 6 in Indexed1 MSB-first: byte 0, bit 1
    Assert.That((raw.PixelData[0] & 0x02) != 0, Is.True, "Seventh pixel should be set");
  }
}
