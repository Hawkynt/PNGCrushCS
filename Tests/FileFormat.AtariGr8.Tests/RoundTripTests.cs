using System;
using System.IO;
using FileFormat.Core;
using FileFormat.AtariGr8;

namespace FileFormat.AtariGr8.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AtariGr8File {
      RawData = new byte[7680]
    };

    var bytes = AtariGr8Writer.ToBytes(original);
    var restored = AtariGr8Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var rawData = new byte[7680];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new AtariGr8File { RawData = rawData };

    var bytes = AtariGr8Writer.ToBytes(original);
    var restored = AtariGr8Reader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternData() {
    var rawData = new byte[7680];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new AtariGr8File { RawData = rawData };

    var bytes = AtariGr8Writer.ToBytes(original);
    var restored = AtariGr8Reader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[7680];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new AtariGr8File { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gr8");
    try {
      var bytes = AtariGr8Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AtariGr8Reader.FromFile(new FileInfo(tempPath));

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
    var rawData = new byte[7680];
    rawData[0] = 0b10110001;
    rawData[1] = 0b11001010;

    var original = new AtariGr8File { RawData = rawData };

    var raw = AtariGr8File.ToRawImage(original);
    var restored = AtariGr8File.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new AtariGr8File { RawData = new byte[7680] };

    var raw = AtariGr8File.ToRawImage(original);
    var restored = AtariGr8File.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllPixelsSet() {
    var rawData = new byte[7680];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new AtariGr8File { RawData = rawData };

    var raw = AtariGr8File.ToRawImage(original);
    var restored = AtariGr8File.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PixelBitOrder_MSBFirst() {
    var rawData = new byte[7680];
    rawData[0] = 0x80;

    var original = new AtariGr8File { RawData = rawData };
    var raw = AtariGr8File.ToRawImage(original);

    Assert.That((raw.PixelData[0] & 0x80) != 0, Is.True, "MSB pixel (0,0) should be set");
    Assert.That((raw.PixelData[0] & 0x40) != 0, Is.False, "Next pixel (1,0) should be clear");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PixelBitOrder_LSBLast() {
    var rawData = new byte[7680];
    rawData[0] = 0x01;

    var original = new AtariGr8File { RawData = rawData };
    var raw = AtariGr8File.ToRawImage(original);

    Assert.That((raw.PixelData[0] & 0x01) != 0, Is.True, "LSB pixel (7,0) should be set");
    Assert.That((raw.PixelData[0] & 0x02) != 0, Is.False, "Pixel (6,0) should be clear");
  }
}
