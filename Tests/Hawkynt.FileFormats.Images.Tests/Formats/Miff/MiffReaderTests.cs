using System;
using System.IO;
using System.Text;
using FileFormat.Miff;

namespace FileFormat.Miff.Tests;

[TestFixture]
public sealed class MiffReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MiffReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MiffReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".miff"));
    Assert.Throws<FileNotFoundException>(() => MiffReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => MiffReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("id=NotMagick\ncolumns=2\nrows=2\n:\n");
    Assert.Throws<InvalidDataException>(() => MiffReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var pixelData = new byte[2 * 2 * 3]; // 2x2, RGB, 3 bytes per pixel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var header = "id=ImageMagick\nclass=DirectClass\ncolumns=2\nrows=2\ndepth=8\ntype=TrueColor\ncolorspace=sRGB\ncompression=None\n:\n\x1A";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = MiffReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Depth, Is.EqualTo(8));
    Assert.That(result.Type, Is.EqualTo("TrueColor"));
    Assert.That(result.ColorClass, Is.EqualTo(MiffColorClass.DirectClass));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MiffReader.FromStream(null!));
  }
}
