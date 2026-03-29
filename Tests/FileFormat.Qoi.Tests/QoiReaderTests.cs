using System;
using System.IO;
using FileFormat.Qoi;

namespace FileFormat.Qoi.Tests;

[TestFixture]
public sealed class QoiReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QoiReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QoiReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".qoi"));
    Assert.Throws<FileNotFoundException>(() => QoiReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QoiReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => QoiReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[QoiHeader.StructSize + 8];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'W';
    Assert.Throws<InvalidDataException>(() => QoiReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var qoi = _BuildMinimalQoi(2, 2, QoiChannels.Rgb);
    var result = QoiReader.FromBytes(qoi);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Channels, Is.EqualTo(QoiChannels.Rgb));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba_ParsesCorrectly() {
    var qoi = _BuildMinimalQoi(3, 2, QoiChannels.Rgba);
    var result = QoiReader.FromBytes(qoi);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Channels, Is.EqualTo(QoiChannels.Rgba));
  }

  private static byte[] _BuildMinimalQoi(int width, int height, QoiChannels channels) {
    var channelCount = (int)channels;
    var pixelData = new byte[width * height * channelCount];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new QoiFile {
      Width = width,
      Height = height,
      Channels = channels,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = pixelData
    };

    return QoiWriter.ToBytes(file);
  }
}
