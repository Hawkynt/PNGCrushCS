using System;
using System.IO;
using FileFormat.UtahRle;

namespace FileFormat.UtahRle.Tests;

[TestFixture]
public sealed class UtahRleReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => UtahRleReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => UtahRleReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rle"));
    Assert.Throws<FileNotFoundException>(() => UtahRleReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => UtahRleReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => UtahRleReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[14];
    bad[0] = 0xFF;
    bad[1] = 0xFF;
    Assert.Throws<InvalidDataException>(() => UtahRleReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var file = new UtahRleFile {
      Width = 2,
      Height = 2,
      NumChannels = 1,
      PixelData = [10, 20, 30, 40]
    };

    var bytes = UtahRleWriter.ToBytes(file);
    var result = UtahRleReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.NumChannels, Is.EqualTo(1));
  }
}
