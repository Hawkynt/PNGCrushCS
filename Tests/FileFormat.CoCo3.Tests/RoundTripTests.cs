using System;
using System.IO;
using FileFormat.Core;
using FileFormat.CoCo3;

namespace FileFormat.CoCo3.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificBytes() {
    var rawData = new byte[32000];
    rawData[0] = 0x3F;
    rawData[1] = 0x01;
    rawData[159] = 0x20;
    rawData[31999] = 0x15;

    var original = new CoCo3File { RawData = rawData };

    var bytes = CoCo3Writer.ToBytes(original);
    var restored = CoCo3Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new CoCo3File { RawData = new byte[32000] };

    var bytes = CoCo3Writer.ToBytes(original);
    var restored = CoCo3Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var rawData = new byte[32000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new CoCo3File { RawData = rawData };

    var bytes = CoCo3Writer.ToBytes(original);
    var restored = CoCo3Reader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[32000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new CoCo3File { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cc3");
    try {
      var bytes = CoCo3Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = CoCo3Reader.FromFile(new FileInfo(tempPath));

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
    var rawData = new byte[32000];
    rawData[0] = 0x12;
    rawData[1] = 0x34;
    rawData[159] = 0xAB;
    rawData[31999] = 0xCD;

    var original = new CoCo3File { RawData = rawData };

    var raw = CoCo3File.ToRawImage(original);
    var restored = CoCo3File.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new CoCo3File { RawData = new byte[32000] };

    var raw = CoCo3File.ToRawImage(original);
    var restored = CoCo3File.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllMax() {
    var rawData = new byte[32000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new CoCo3File { RawData = rawData };

    var raw = CoCo3File.ToRawImage(original);
    var restored = CoCo3File.FromRawImage(raw);

    // Only low nibbles matter (0-15 each), so 0xFF packs as (15 << 4) | 15 = 0xFF
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gradient() {
    var rawData = new byte[32000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new CoCo3File { RawData = rawData };

    var raw = CoCo3File.ToRawImage(original);
    var restored = CoCo3File.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_NibbleBoundary() {
    var rawData = new byte[32000];
    rawData[0] = 0x10;  // high=1, low=0
    rawData[1] = 0x0F;  // high=0, low=15
    rawData[2] = 0xA5;  // high=10, low=5

    var original = new CoCo3File { RawData = rawData };

    var raw = CoCo3File.ToRawImage(original);

    Assert.That(raw.PixelData[0], Is.EqualTo(1));
    Assert.That(raw.PixelData[1], Is.EqualTo(0));
    Assert.That(raw.PixelData[2], Is.EqualTo(0));
    Assert.That(raw.PixelData[3], Is.EqualTo(15));
    Assert.That(raw.PixelData[4], Is.EqualTo(10));
    Assert.That(raw.PixelData[5], Is.EqualTo(5));

    var restored = CoCo3File.FromRawImage(raw);

    Assert.That(restored.RawData[0], Is.EqualTo(0x10));
    Assert.That(restored.RawData[1], Is.EqualTo(0x0F));
    Assert.That(restored.RawData[2], Is.EqualTo(0xA5));
  }
}
