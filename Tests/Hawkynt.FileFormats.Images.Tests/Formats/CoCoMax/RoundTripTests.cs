using System;
using System.IO;
using FileFormat.Core;
using FileFormat.CoCoMax;

namespace FileFormat.CoCoMax.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificBytes() {
    var rawData = new byte[6144];
    rawData[0] = 0x3F;
    rawData[1] = 0x01;
    rawData[2] = 0x20;
    rawData[6143] = 0x15;

    var original = new CoCoMaxFile { RawData = rawData };

    var bytes = CoCoMaxWriter.ToBytes(original);
    var restored = CoCoMaxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new CoCoMaxFile { RawData = new byte[6144] };

    var bytes = CoCoMaxWriter.ToBytes(original);
    var restored = CoCoMaxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var rawData = new byte[6144];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new CoCoMaxFile { RawData = rawData };

    var bytes = CoCoMaxWriter.ToBytes(original);
    var restored = CoCoMaxReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[6144];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new CoCoMaxFile { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".max");
    try {
      var bytes = CoCoMaxWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = CoCoMaxReader.FromFile(new FileInfo(tempPath));

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
    rawData[0] = 0xFF;
    rawData[1] = 0xA5;
    rawData[31] = 0x0F;
    rawData[6143] = 0xC3;

    var original = new CoCoMaxFile { RawData = rawData };

    var raw = CoCoMaxFile.ToRawImage(original);
    var restored = CoCoMaxFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new CoCoMaxFile { RawData = new byte[6144] };

    var raw = CoCoMaxFile.ToRawImage(original);
    var restored = CoCoMaxFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllOnes() {
    var rawData = new byte[6144];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new CoCoMaxFile { RawData = rawData };

    var raw = CoCoMaxFile.ToRawImage(original);
    var restored = CoCoMaxFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gradient() {
    var rawData = new byte[6144];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new CoCoMaxFile { RawData = rawData };

    var raw = CoCoMaxFile.ToRawImage(original);
    var restored = CoCoMaxFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}
