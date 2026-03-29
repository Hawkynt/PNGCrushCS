using System;
using System.IO;
using FileFormat.Core;
using FileFormat.C128VDC;

namespace FileFormat.C128VDC.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[C128VDCFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new C128VDCFile { RawData = rawData };

    var bytes = C128VDCWriter.ToBytes(original);
    var restored = C128VDCReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new C128VDCFile {
      RawData = new byte[C128VDCFile.ExpectedFileSize]
    };

    var bytes = C128VDCWriter.ToBytes(original);
    var restored = C128VDCReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(640));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var rawData = new byte[C128VDCFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new C128VDCFile { RawData = rawData };

    var bytes = C128VDCWriter.ToBytes(original);
    var restored = C128VDCReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[C128VDCFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new C128VDCFile { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vdc");
    try {
      var bytes = C128VDCWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = C128VDCReader.FromFile(new FileInfo(tempPath));

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
    var rawData = new byte[C128VDCFile.ExpectedFileSize];
    rawData[0] = 0xAA;
    rawData[79] = 0x55;
    rawData[80] = 0xCC;

    var original = new C128VDCFile { RawData = rawData };

    var raw = C128VDCFile.ToRawImage(original);
    var restored = C128VDCFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllZeros() {
    var original = new C128VDCFile {
      RawData = new byte[C128VDCFile.ExpectedFileSize]
    };

    var raw = C128VDCFile.ToRawImage(original);
    var restored = C128VDCFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_AllOnes() {
    var rawData = new byte[C128VDCFile.ExpectedFileSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = 0xFF;

    var original = new C128VDCFile { RawData = rawData };

    var raw = C128VDCFile.ToRawImage(original);
    var restored = C128VDCFile.FromRawImage(raw);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FirstPixelSet() {
    var rawData = new byte[C128VDCFile.ExpectedFileSize];
    rawData[0] = 0x80; // MSB = first pixel set

    var original = new C128VDCFile { RawData = rawData };

    var raw = C128VDCFile.ToRawImage(original);

    Assert.That((raw.PixelData[0] & 0x80) != 0, Is.True, "First pixel should be set");

    var restored = C128VDCFile.FromRawImage(raw);
    Assert.That(restored.RawData[0], Is.EqualTo(0x80));
  }
}
