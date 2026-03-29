using System;
using FileFormat.GunPaint;

namespace FileFormat.GunPaint.Tests;

[TestFixture]
public sealed class GunPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GunPaintWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly33603Bytes() {
    var file = _BuildValidFile();
    var bytes = GunPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(GunPaintFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LoadAddress_WrittenAsLittleEndian() {
    var file = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = new byte[GunPaintFile.RawDataSize]
    };

    var bytes = GunPaintWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RawData_StartsAtByte2() {
    var rawData = new byte[GunPaintFile.RawDataSize];
    rawData[0] = 0xAA;
    rawData[GunPaintFile.RawDataSize - 1] = 0xBB;

    var file = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = rawData
    };

    var bytes = GunPaintWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xAA));
    Assert.That(bytes[GunPaintFile.ExpectedFileSize - 1], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShorterRawData_PaddedWithZeros() {
    var rawData = new byte[100];
    Array.Fill(rawData, (byte)0xFF);

    var file = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = rawData
    };

    var bytes = GunPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(GunPaintFile.ExpectedFileSize));
    Assert.That(bytes[2], Is.EqualTo(0xFF));
    Assert.That(bytes[101], Is.EqualTo(0xFF));
    Assert.That(bytes[102], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyRawData_ProducesCorrectSize() {
    var file = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = []
    };

    var bytes = GunPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(GunPaintFile.ExpectedFileSize));
    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x40));
  }

  private static GunPaintFile _BuildValidFile() {
    var rawData = new byte[GunPaintFile.RawDataSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    return new() {
      LoadAddress = 0x4000,
      RawData = rawData
    };
  }
}
