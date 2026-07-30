using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Stad;

namespace FileFormat.Stad.Tests;

[TestFixture]
public sealed class StadReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => StadReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pac"));
    Assert.Throws<FileNotFoundException>(() => StadReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => StadReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => StadReader.FromBytes(new byte[2]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RawUncompressed_Parses() {
    var data = new byte[32000];
    data[0] = 0xAB;
    data[31999] = 0xCD;

    var result = StadReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(400));
    Assert.That(result.RawData.Length, Is.EqualTo(32000));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[31999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithMagic_Parses() {
    // Build a simple compressed file: magic + literal of 2 bytes
    var compressed = new byte[] {
      (byte)'p', (byte)'M', (byte)'8', (byte)'6',
      1, 0xAA, 0xBB // literal: 2 bytes
    };

    var result = StadReader.FromBytes(compressed);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(400));
    Assert.That(result.RawData[0], Is.EqualTo(0xAA));
    Assert.That(result.RawData[1], Is.EqualTo(0xBB));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriteThenRead_PreservesData() {
    var rawData = new byte[32000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new StadFile { RawData = rawData };

    var bytes = StadWriter.ToBytes(original);
    var restored = StadReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new StadFile { RawData = new byte[32000] };

    var bytes = StadWriter.ToBytes(original);
    var restored = StadReader.FromBytes(bytes);

    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[32000];
    rawData[0] = 0xFF;
    rawData[31999] = 0xAA;
    var original = new StadFile { RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pac");
    try {
      File.WriteAllBytes(tempPath, StadWriter.ToBytes(original));
      var restored = StadReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}

