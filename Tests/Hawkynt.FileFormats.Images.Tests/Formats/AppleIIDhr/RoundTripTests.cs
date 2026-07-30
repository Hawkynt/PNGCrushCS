using System;
using System.IO;
using FileFormat.Core;
using FileFormat.AppleIIDhr;

namespace FileFormat.AppleIIDhr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AppleIIDhrFile {
      RawData = new byte[16384]
    };

    var bytes = AppleIIDhrWriter.ToBytes(original);
    var restored = AppleIIDhrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(560));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var rawData = new byte[16384];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new AppleIIDhrFile { RawData = rawData };

    var bytes = AppleIIDhrWriter.ToBytes(original);
    var restored = AppleIIDhrReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[16384];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new AppleIIDhrFile { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dhr");
    try {
      var bytes = AppleIIDhrWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AppleIIDhrReader.FromFile(new FileInfo(tempPath));

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
    var original = new AppleIIDhrFile { RawData = new byte[16384] };

    var raw = AppleIIDhrFile.ToRawImage(original);
    var restored = AppleIIDhrFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PreservesDataBits() {
    var rawData = new byte[16384];
    // Aux bank byte 0 (row 0, col 0)
    rawData[0] = 0x55;
    // Main bank byte 0 (row 0, col 0)
    rawData[8192] = 0x2A;
    // Aux bank byte at interleaved row 1
    rawData[1024] = 0x7F;

    var original = new AppleIIDhrFile { RawData = rawData };

    var raw = AppleIIDhrFile.ToRawImage(original);
    var restored = AppleIIDhrFile.FromRawImage(raw);

    // Only the lower 7 bits matter per byte
    for (var y = 0; y < 192; ++y) {
      var offset = AppleIIDhrFile.GetRowOffset(y);
      for (var col = 0; col < 40; ++col) {
        var auxIdx = offset + col;
        var mainIdx = 8192 + offset + col;
        Assert.That(restored.RawData[auxIdx] & 0x7F, Is.EqualTo(original.RawData[auxIdx] & 0x7F), $"Aux row {y}, col {col} mismatch");
        Assert.That(restored.RawData[mainIdx] & 0x7F, Is.EqualTo(original.RawData[mainIdx] & 0x7F), $"Main row {y}, col {col} mismatch");
      }
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllPixelsSet() {
    var rawData = new byte[16384];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0x7F; // all 7 data bits set

    var original = new AppleIIDhrFile { RawData = rawData };

    var raw = AppleIIDhrFile.ToRawImage(original);
    var restored = AppleIIDhrFile.FromRawImage(raw);

    for (var y = 0; y < 192; ++y) {
      var offset = AppleIIDhrFile.GetRowOffset(y);
      for (var col = 0; col < 40; ++col) {
        Assert.That(restored.RawData[offset + col] & 0x7F, Is.EqualTo(0x7F), $"Aux row {y}, col {col} mismatch");
        Assert.That(restored.RawData[8192 + offset + col] & 0x7F, Is.EqualTo(0x7F), $"Main row {y}, col {col} mismatch");
      }
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Interleave_Row0_OffsetIs0() {
    Assert.That(AppleIIDhrFile.GetRowOffset(0), Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Interleave_Row1_OffsetIs1024() {
    Assert.That(AppleIIDhrFile.GetRowOffset(1), Is.EqualTo(1024));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Interleave_SameAsHgr() {
    // DHGR uses the same interleave formula as HGR
    for (var y = 0; y < 192; ++y) {
      var expected = (y % 8) * 1024 + (y / 64) * 40 + ((y / 8) % 8) * 128;
      Assert.That(AppleIIDhrFile.GetRowOffset(y), Is.EqualTo(expected), $"Row {y} interleave mismatch");
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AuxPixelFirst_MainPixelSecond() {
    var rawData = new byte[16384];
    // Set bit 0 in aux byte 0 of row 0 -> pixel at x=0
    rawData[0] = 0x01;
    // Set bit 0 in main byte 0 of row 0 -> pixel at x=7
    rawData[8192] = 0x01;

    var file = new AppleIIDhrFile { RawData = rawData };
    var raw = AppleIIDhrFile.ToRawImage(file);

    // Pixel (0,0) in Indexed1 MSB-first: byte 0, bit 7
    Assert.That((raw.PixelData[0] & 0x80) != 0, Is.True, "Aux bit 0 should map to pixel 0");
    // Pixel (7,0) in Indexed1 MSB-first: byte 0, bit 0
    Assert.That((raw.PixelData[0] & 0x01) != 0, Is.True, "Main bit 0 should map to pixel 7");
  }
}
