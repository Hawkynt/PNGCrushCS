using System;
using System.IO;
using FileFormat.Gd2;

namespace FileFormat.Gd2.Tests;

[TestFixture]
public sealed class Gd2ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Gd2Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Gd2Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gd2"));
    Assert.Throws<FileNotFoundException>(() => Gd2Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Gd2Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => Gd2Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var data = _BuildMinimalGd2(2, 2);
    data[0] = 0xFF;
    Assert.Throws<InvalidDataException>(() => Gd2Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CompressedFormat_ThrowsInvalidDataException() {
    var data = _BuildMinimalGd2(2, 2);
    // Set format to 2 (compressed) at offset 12-13 BE
    data[12] = 0;
    data[13] = 2;
    Assert.Throws<InvalidDataException>(() => Gd2Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InsufficientPixelData_ThrowsInvalidDataException() {
    // Header says 2x2 = 16 pixel bytes needed, but only provide header
    var data = new byte[Gd2File.HeaderSize];
    Gd2File.Signature.CopyTo(data.AsSpan());
    data[4] = 0; data[5] = 2;   // version
    data[6] = 0; data[7] = 2;   // width
    data[8] = 0; data[9] = 2;   // height
    data[10] = 0; data[11] = 2; // chunk size
    data[12] = 0; data[13] = 1; // format raw
    data[14] = 0; data[15] = 1; // xChunkCount
    data[16] = 0; data[17] = 1; // yChunkCount

    Assert.Throws<InvalidDataException>(() => Gd2Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesDimensions() {
    var data = _BuildMinimalGd2(4, 3);
    var result = Gd2Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesVersion() {
    var data = _BuildMinimalGd2(2, 2);
    var result = Gd2Reader.FromBytes(data);
    Assert.That(result.Version, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesFormat() {
    var data = _BuildMinimalGd2(2, 2);
    var result = Gd2Reader.FromBytes(data);
    Assert.That(result.Format, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesChunkSize() {
    var data = _BuildMinimalGd2(3, 5);
    var result = Gd2Reader.FromBytes(data);
    Assert.That(result.ChunkSize, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_PixelDataPreserved() {
    var data = _BuildMinimalGd2(1, 1);
    // Set pixel at offset 18: A=0(opaque), R=0xFF, G=0x80, B=0x40
    data[18] = 0x00;
    data[19] = 0xFF;
    data[20] = 0x80;
    data[21] = 0x40;

    var result = Gd2Reader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0x00));
    Assert.That(result.PixelData[1], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[2], Is.EqualTo(0x80));
    Assert.That(result.PixelData[3], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_Parses() {
    var data = _BuildMinimalGd2(2, 2);
    using var ms = new MemoryStream(data);
    var result = Gd2Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroDimensions_ThrowsInvalidDataException() {
    var data = _BuildMinimalGd2(1, 1);
    // Set width to 0
    data[6] = 0;
    data[7] = 0;
    Assert.Throws<InvalidDataException>(() => Gd2Reader.FromBytes(data));
  }

  private static byte[] _BuildMinimalGd2(int width, int height) {
    var chunkSize = Math.Max(width, height);
    var pixelBytes = width * height * 4;
    var data = new byte[Gd2File.HeaderSize + pixelBytes];
    var span = data.AsSpan();

    Gd2File.Signature.CopyTo(span);
    data[4] = 0; data[5] = 2;                          // version = 2
    data[6] = (byte)(width >> 8); data[7] = (byte)width;   // width BE
    data[8] = (byte)(height >> 8); data[9] = (byte)height; // height BE
    data[10] = (byte)(chunkSize >> 8); data[11] = (byte)chunkSize; // chunk size BE
    data[12] = 0; data[13] = 1;                         // format = raw
    data[14] = 0; data[15] = 1;                         // xChunkCount
    data[16] = 0; data[17] = 1;                         // yChunkCount

    return data;
  }
}
