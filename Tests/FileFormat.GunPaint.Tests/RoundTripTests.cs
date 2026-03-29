using System;
using System.IO;
using FileFormat.GunPaint;

namespace FileFormat.GunPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_PreservesData() {
    var original = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = new byte[GunPaintFile.RawDataSize]
    };

    var bytes = GunPaintWriter.ToBytes(original);
    var restored = GunPaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternData_PreservesAllBytes() {
    var rawData = new byte[GunPaintFile.RawDataSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 7 % 256);

    var original = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = rawData
    };

    var bytes = GunPaintWriter.ToBytes(original);
    var restored = GunPaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress_Preserved() {
    var original = new GunPaintFile {
      LoadAddress = 0x6000,
      RawData = new byte[GunPaintFile.RawDataSize]
    };

    var bytes = GunPaintWriter.ToBytes(original);
    var restored = GunPaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue_Preserved() {
    var rawData = new byte[GunPaintFile.RawDataSize];
    Array.Fill(rawData, (byte)0xFF);

    var original = new GunPaintFile {
      LoadAddress = 0xFFFF,
      RawData = rawData
    };

    var bytes = GunPaintWriter.ToBytes(original);
    var restored = GunPaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var rawData = new byte[GunPaintFile.RawDataSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = rawData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gun");
    try {
      var bytes = GunPaintWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = GunPaintReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = new byte[GunPaintFile.RawDataSize]
    };

    var bytes = GunPaintWriter.ToBytes(original);
    var restored = GunPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(160));
    Assert.That(restored.Height, Is.EqualTo(200));
  }
}
