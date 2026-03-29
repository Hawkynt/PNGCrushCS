using System;
using System.IO;
using FileFormat.Flif;

namespace FileFormat.Flif.Tests;

[TestFixture]
public sealed class FlifReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FlifReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FlifReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flif"));
    Assert.Throws<FileNotFoundException>(() => FlifReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FlifReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => FlifReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[32];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'W';
    Assert.Throws<InvalidDataException>(() => FlifReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var file = _BuildMinimalFlif(2, 2, FlifChannelCount.Gray);
    var result = FlifReader.FromBytes(file);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ChannelCount, Is.EqualTo(FlifChannelCount.Gray));
    Assert.That(result.BitsPerChannel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var file = _BuildMinimalFlif(3, 2, FlifChannelCount.Rgb);
    var result = FlifReader.FromBytes(file);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ChannelCount, Is.EqualTo(FlifChannelCount.Rgb));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba_ParsesCorrectly() {
    var file = _BuildMinimalFlif(4, 3, FlifChannelCount.Rgba);
    var result = FlifReader.FromBytes(file);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.ChannelCount, Is.EqualTo(FlifChannelCount.Rgba));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgb_ParsesCorrectly() {
    var bytes = _BuildMinimalFlif(2, 2, FlifChannelCount.Rgb);
    using var stream = new MemoryStream(bytes);
    var result = FlifReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ChannelCount, Is.EqualTo(FlifChannelCount.Rgb));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidChannelCount_ThrowsInvalidDataException() {
    var bytes = _BuildMinimalFlif(2, 2, FlifChannelCount.Rgb);
    // Corrupt the channel count bits (set to 5 which is invalid)
    bytes[4] = (byte)((bytes[4] & 0xF8) | 5);
    Assert.Throws<InvalidDataException>(() => FlifReader.FromBytes(bytes));
  }

  private static byte[] _BuildMinimalFlif(int width, int height, FlifChannelCount channels) {
    var channelCount = (int)channels;
    var pixelData = new byte[width * height * channelCount];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new FlifFile {
      Width = width,
      Height = height,
      ChannelCount = channels,
      BitsPerChannel = 8,
      PixelData = pixelData
    };

    return FlifWriter.ToBytes(file);
  }
}
