using System;
using System.IO;
using FileFormat.Png;

namespace FileFormat.Png.Tests;

[TestFixture]
public sealed class PngReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PngReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PngReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png"));
    Assert.Throws<FileNotFoundException>(() => PngReader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PngReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[8];
    Assert.Throws<InvalidDataException>(() => PngReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var data = new byte[33];
    data[0] = 0xFF;
    Assert.Throws<InvalidDataException>(() => PngReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var pixelData = new byte[2][];
    for (var y = 0; y < 2; ++y) {
      pixelData[y] = new byte[6];
      for (var x = 0; x < 6; ++x)
        pixelData[y][x] = (byte)(y * 6 + x);
    }

    var original = new PngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = pixelData
    };

    var bytes = PngWriter.ToBytes(original);
    var result = PngReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BitDepth, Is.EqualTo(8));
    Assert.That(result.ColorType, Is.EqualTo(PngColorType.RGB));
  }
}
