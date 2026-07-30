using System;
using System.IO;
using FileFormat.JpegLs;

namespace FileFormat.JpegLs.Tests;

[TestFixture]
public sealed class JpegLsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegLsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegLsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jls"));
    Assert.Throws<FileNotFoundException>(() => JpegLsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegLsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => JpegLsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    Assert.Throws<InvalidDataException>(() => JpegLsReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesDimensions() {
    var jls = _BuildMinimalJls(4, 3, 1);
    var result = JpegLsReader.FromBytes(jls);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.ComponentCount, Is.EqualTo(1));
    Assert.That(result.BitsPerSample, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesDimensions() {
    var jls = _BuildMinimalJls(3, 2, 3);
    var result = JpegLsReader.FromBytes(jls);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ComponentCount, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_PixelDataLength() {
    var jls = _BuildMinimalJls(5, 4, 1);
    var result = JpegLsReader.FromBytes(jls);

    Assert.That(result.PixelData, Has.Length.EqualTo(5 * 4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_PixelDataLength() {
    var jls = _BuildMinimalJls(2, 2, 3);
    var result = JpegLsReader.FromBytes(jls);

    Assert.That(result.PixelData, Has.Length.EqualTo(2 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGrayscale_ParsesCorrectly() {
    var jls = _BuildMinimalJls(2, 2, 1);
    using var ms = new MemoryStream(jls);
    var result = JpegLsReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_StartsWithSoiMarker() {
    var jls = _BuildMinimalJls(2, 2, 1);

    Assert.That(jls[0], Is.EqualTo(0xFF));
    Assert.That(jls[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_EndsWithEoiMarker() {
    var jls = _BuildMinimalJls(2, 2, 1);

    Assert.That(jls[^2], Is.EqualTo(0xFF));
    Assert.That(jls[^1], Is.EqualTo(0xD9));
  }

  private static byte[] _BuildMinimalJls(int width, int height, int componentCount) {
    var pixelData = new byte[width * height * componentCount];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new JpegLsFile {
      Width = width,
      Height = height,
      BitsPerSample = 8,
      ComponentCount = componentCount,
      PixelData = pixelData
    };

    return JpegLsWriter.ToBytes(file);
  }
}
