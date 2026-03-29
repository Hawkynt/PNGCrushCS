using System;
using System.IO;
using FileFormat.Jng;

namespace FileFormat.Jng.Tests;

[TestFixture]
public sealed class JngReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JngReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JngReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jng"));
    Assert.Throws<FileNotFoundException>(() => JngReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JngReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => JngReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[100];
    bad[0] = 0xFF;
    bad[1] = 0x00;
    Assert.Throws<InvalidDataException>(() => JngReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidColor_ParsesCorrectly() {
    var bytes = _BuildMinimalJng(64, 48, 10, 8);
    var result = JngReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(64));
    Assert.That(result.Height, Is.EqualTo(48));
    Assert.That(result.ColorType, Is.EqualTo(10));
    Assert.That(result.ImageSampleDepth, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGray_ParsesCorrectly() {
    var bytes = _BuildMinimalJng(32, 32, 8, 8);
    var result = JngReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(32));
    Assert.That(result.Height, Is.EqualTo(32));
    Assert.That(result.ColorType, Is.EqualTo(8));
  }

  private static byte[] _BuildMinimalJng(int width, int height, byte colorType, byte sampleDepth) {
    var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }; // Minimal JPEG: SOI + EOI

    var file = new JngFile {
      Width = width,
      Height = height,
      ColorType = colorType,
      ImageSampleDepth = sampleDepth,
      AlphaSampleDepth = 0,
      AlphaCompression = JngAlphaCompression.PngDeflate,
      JpegData = jpegData,
      AlphaData = null
    };

    return JngWriter.ToBytes(file);
  }
}
