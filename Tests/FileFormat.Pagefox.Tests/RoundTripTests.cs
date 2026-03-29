using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Pagefox;

namespace FileFormat.Pagefox.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_DataPreserved() {
    var rawData = new byte[16384];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new PagefoxFile { RawData = rawData };
    var bytes = PagefoxWriter.ToBytes(original);
    var restored = PagefoxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new PagefoxFile { RawData = new byte[16384] };

    var bytes = PagefoxWriter.ToBytes(original);
    var restored = PagefoxReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[16384];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new PagefoxFile { RawData = rawData };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pfx");
    try {
      var bytes = PagefoxWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PagefoxReader.FromFile(new FileInfo(tempPath));

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
    var rawData = new byte[16384];
    rawData[0] = 0xFF;
    rawData[1] = 0xAA;
    rawData[79] = 0x55;

    var original = new PagefoxFile { RawData = rawData };
    var raw = PagefoxFile.ToRawImage(original);
    var restored = PagefoxFile.FromRawImage(raw);

    // Active data area (first 16000 bytes) should round-trip
    for (var i = 0; i < 16000; ++i)
      Assert.That(restored.RawData[i], Is.EqualTo(original.RawData[i]), $"Byte {i} mismatch");
  }
}
