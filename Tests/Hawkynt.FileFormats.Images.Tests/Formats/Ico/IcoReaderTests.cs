using System;
using System.IO;
using System.Linq;
using FileFormat.Ico;
using FileFormat.Png;

namespace FileFormat.Ico.Tests;

[TestFixture]
public sealed class IcoReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcoReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcoReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ico"));
    Assert.Throws<FileNotFoundException>(() => IcoReader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[4];
    Assert.Throws<InvalidDataException>(() => IcoReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidType_ThrowsInvalidDataException() {
    var data = new byte[6];
    data[0] = 0; data[1] = 0; // reserved = 0
    data[2] = 99; data[3] = 0; // type = 99 (invalid, should be 1)
    data[4] = 0; data[5] = 0; // count = 0
    Assert.Throws<InvalidDataException>(() => IcoReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidIco_ParsesCorrectly() {
    var pngFile = new PngFile {
      Width = 16, Height = 16, BitDepth = 8, ColorType = PngColorType.RGBA,
      PixelData = Enumerable.Range(0, 16).Select(_ => new byte[64]).ToArray()
    };
    var pngBytes = PngWriter.ToBytes(pngFile);

    var icoFile = new IcoFile {
      Images = [
        new IcoImage { Width = 16, Height = 16, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes }
      ]
    };

    var icoBytes = IcoWriter.ToBytes(icoFile);
    var result = IcoReader.FromBytes(icoBytes);

    Assert.That(result.Images.Count, Is.EqualTo(1));
    Assert.That(result.Images[0].Width, Is.EqualTo(16));
    Assert.That(result.Images[0].Height, Is.EqualTo(16));
    Assert.That(result.Images[0].Format, Is.EqualTo(IcoImageFormat.Png));
    Assert.That(result.Images[0].BitsPerPixel, Is.EqualTo(32));
  }
}
