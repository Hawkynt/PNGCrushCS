using System;
using System.IO;
using FileFormat.GoDot4Bit;

namespace FileFormat.GoDot4Bit.Tests;

[TestFixture]
public sealed class GoDot4BitReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GoDot4BitReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GoDot4BitReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".4bt"));
    Assert.Throws<FileNotFoundException>(() => GoDot4BitReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GoDot4BitReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => GoDot4BitReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => GoDot4BitReader.FromBytes(new byte[16385]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[GoDot4BitFile.ExpectedFileSize];
    data[0] = 0x12;
    data[1] = 0x34;

    var result = GoDot4BitReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.PixelData.Length, Is.EqualTo(16384));
    Assert.That(result.PixelData[0], Is.EqualTo(0x12));
    Assert.That(result.PixelData[1], Is.EqualTo(0x34));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = new byte[GoDot4BitFile.ExpectedFileSize];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = GoDot4BitReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesPreserved() {
    var pixelData = new byte[16384];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new GoDot4BitFile {
      PixelData = pixelData,
    };

    var bytes = GoDot4BitWriter.ToBytes(original);
    var restored = GoDot4BitReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
