using System;
using System.IO;
using System.Text;
using FileFormat.Nrrd;

namespace FileFormat.Nrrd.Tests;

[TestFixture]
public sealed class NrrdReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NrrdReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NrrdReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".nrrd"));
    Assert.Throws<FileNotFoundException>(() => NrrdReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NrrdReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => NrrdReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[20];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'W';
    Assert.Throws<InvalidDataException>(() => NrrdReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidUInt8_ParsesCorrectly() {
    var header = "NRRD0004\ntype: uint8\ndimension: 2\nsizes: 3 2\nencoding: raw\nendian: little\n\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[] { 10, 20, 30, 40, 50, 60 };
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = NrrdReader.FromBytes(data);

    Assert.That(result.Sizes, Is.EqualTo(new[] { 3, 2 }));
    Assert.That(result.DataType, Is.EqualTo(NrrdType.UInt8));
    Assert.That(result.Encoding, Is.EqualTo(NrrdEncoding.Raw));
    Assert.That(result.PixelData.Length, Is.EqualTo(6));
    Assert.That(result.PixelData[0], Is.EqualTo(10));
    Assert.That(result.PixelData[5], Is.EqualTo(60));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var header = "NRRD0004\ntype: uint8\ndimension: 1\nsizes: 4\nencoding: raw\n\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    using var ms = new MemoryStream(data);
    var result = NrrdReader.FromStream(ms);

    Assert.That(result.Sizes, Is.EqualTo(new[] { 4 }));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
  }
}
