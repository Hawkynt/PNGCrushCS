using System;
using System.IO;
using FileFormat.Exr;

namespace FileFormat.Exr.Tests;

[TestFixture]
public sealed class ExrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ExrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ExrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exr"));
    Assert.Throws<FileNotFoundException>(() => ExrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ExrReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => ExrReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[8];
    bad[0] = 0xFF;
    bad[1] = 0xFF;
    bad[2] = 0xFF;
    bad[3] = 0xFF;
    Assert.Throws<InvalidDataException>(() => ExrReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleChannelFloat_ParsesCorrectly() {
    var exrBytes = ExrWriter.ToBytes(new ExrFile {
      Width = 2,
      Height = 2,
      Compression = ExrCompression.None,
      LineOrder = ExrLineOrder.IncreasingY,
      Channels = [new ExrChannel { Name = "R", PixelType = ExrPixelType.Float, XSampling = 1, YSampling = 1 }],
      PixelData = new byte[2 * 2 * 4]
    });

    var result = ExrReader.FromBytes(exrBytes);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Compression, Is.EqualTo(ExrCompression.None));
    Assert.That(result.Channels, Has.Count.EqualTo(1));
    Assert.That(result.Channels[0].Name, Is.EqualTo("R"));
    Assert.That(result.Channels[0].PixelType, Is.EqualTo(ExrPixelType.Float));
  }
}
