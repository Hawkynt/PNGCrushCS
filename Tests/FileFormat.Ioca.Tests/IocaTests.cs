using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Ioca;

namespace FileFormat.Ioca.Tests;

[TestFixture]
public sealed class IocaReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IocaReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ica"));
    Assert.Throws<FileNotFoundException>(() => IocaReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IocaReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IocaReader.FromBytes(new byte[4]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSimpleData_Parses() {
    // 4-byte header: width=8 (BE), height=2 (BE), then 2 bytes pixel data
    var data = new byte[] {
      0x00, 0x08, // width = 8
      0x00, 0x02, // height = 2
      0xFF,       // row 0: all bits set
      0xAA,       // row 1: alternating
      0x00, 0x00, 0x00, 0x00, // padding to reach MinHeaderSize
    };

    var result = IocaReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0xAA));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_WriteThenRead_PreservesData() {
    var pixelData = new byte[] { 0xFF, 0xAA, 0x55, 0x00 };
    var original = new IocaFile { Width = 16, Height = 2, PixelData = pixelData };

    var bytes = IocaWriter.ToBytes(original);
    var restored = IocaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[] { 0xAA, 0x55 };
    var original = new IocaFile { Width = 8, Height = 2, PixelData = pixelData };

    var raw = IocaFile.ToRawImage(original);
    var restored = IocaFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[] { 0xDE, 0xAD };
    var original = new IocaFile { Width = 8, Height = 2, PixelData = pixelData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ica");
    try {
      File.WriteAllBytes(tempPath, IocaWriter.ToBytes(original));
      var restored = IocaReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}

