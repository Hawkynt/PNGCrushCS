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
    var rawData = _Valid();
    rawData[CoCoMaxFile.BitmapOffset + 0] = 0x3F;
    rawData[CoCoMaxFile.BitmapOffset + 1] = 0x01;
    rawData[CoCoMaxFile.BitmapOffset + 2] = 0x20;
    rawData[CoCoMaxFile.BitmapOffset + CoCoMaxFile.BitmapSize - 1] = 0x15;

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
    var original = new CoCoMaxFile { RawData = _Valid() };

    var bytes = CoCoMaxWriter.ToBytes(original);
    var restored = CoCoMaxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var rawData = _Valid();
    // Only the bitmap: the header is structural and the writer restores it.
    for (var i = 0; i < CoCoMaxFile.BitmapSize; ++i)
      rawData[CoCoMaxFile.BitmapOffset + i] = 0xFF;

    var original = new CoCoMaxFile { RawData = rawData };

    var bytes = CoCoMaxWriter.ToBytes(original);
    var restored = CoCoMaxReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = _Valid();
    for (var i = 0; i < CoCoMaxFile.BitmapSize; ++i)
      rawData[CoCoMaxFile.BitmapOffset + i] = (byte)(i * 7 % 256);

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
    var rawData = _Valid();
    rawData[CoCoMaxFile.BitmapOffset + 0] = 0xFF;
    rawData[CoCoMaxFile.BitmapOffset + 1] = 0xA5;
    rawData[31] = 0x0F;
    rawData[CoCoMaxFile.BitmapOffset + CoCoMaxFile.BitmapSize - 1] = 0xC3;

    var original = new CoCoMaxFile { RawData = rawData };

    var raw = CoCoMaxFile.ToRawImage(original);
    var restored = CoCoMaxFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new CoCoMaxFile { RawData = _Valid() };

    var raw = CoCoMaxFile.ToRawImage(original);
    var restored = CoCoMaxFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllOnes() {
    var rawData = _Valid();
    // Only the bitmap: the header is structural and the writer restores it.
    for (var i = 0; i < CoCoMaxFile.BitmapSize; ++i)
      rawData[CoCoMaxFile.BitmapOffset + i] = 0xFF;

    var original = new CoCoMaxFile { RawData = rawData };

    var raw = CoCoMaxFile.ToRawImage(original);
    var restored = CoCoMaxFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gradient() {
    var rawData = _Valid();
    for (var i = 0; i < CoCoMaxFile.BitmapSize; ++i)
      rawData[CoCoMaxFile.BitmapOffset + i] = (byte)(i * 13 % 256);

    var original = new CoCoMaxFile { RawData = rawData };

    var raw = CoCoMaxFile.ToRawImage(original);
    var restored = CoCoMaxFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  /// <summary>A file of the smallest legal length, carrying the header a reader checks.</summary>
  private static byte[] _Valid() {
    var data = new byte[CoCoMaxFile.ExpectedFileSize];
    CoCoMaxFile.WriteHeader(data);

    return data;
  }
}
