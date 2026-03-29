using System;
using System.IO;
using FileFormat.Clp;

namespace FileFormat.Clp.Tests;

[TestFixture]
public sealed class ClpReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ClpReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ClpReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".clp"));
    Assert.Throws<FileNotFoundException>(() => ClpReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ClpReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => ClpReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFileId_ThrowsInvalidDataException() {
    var bad = new byte[100];
    bad[0] = 0x00;
    bad[1] = 0x00;
    Assert.Throws<InvalidDataException>(() => ClpReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidDib_ParsesCorrectly() {
    var data = _BuildMinimalClpRgb24(4, 3);
    var result = ClpReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.BitsPerPixel, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidDib_ParsesCorrectly() {
    var data = _BuildMinimalClpRgb24(2, 2);
    using var stream = new MemoryStream(data);
    var result = ClpReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  private static byte[] _BuildMinimalClpRgb24(int width, int height) {
    var bytesPerRow = ((width * 24 + 31) / 32) * 4;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new ClpFile {
      Width = width,
      Height = height,
      BitsPerPixel = 24,
      PixelData = pixelData
    };

    return ClpWriter.ToBytes(file);
  }
}
