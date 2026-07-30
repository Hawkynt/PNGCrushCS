using System;
using System.IO;
using FileFormat.Xcf;

namespace FileFormat.Xcf.Tests;

[TestFixture]
public sealed class XcfReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XcfReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XcfReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xcf"));
    Assert.Throws<FileNotFoundException>(() => XcfReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XcfReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => XcfReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[64];
    bad[0] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => XcfReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var xcf = XcfWriter.ToBytes(new XcfFile {
      Width = 2,
      Height = 2,
      ColorMode = XcfColorMode.Rgb,
      Version = 1,
      PixelData = new byte[2 * 2 * 4] // RGBA
    });
    var result = XcfReader.FromBytes(xcf);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ColorMode, Is.EqualTo(XcfColorMode.Rgb));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var xcf = XcfWriter.ToBytes(new XcfFile {
      Width = 4,
      Height = 4,
      ColorMode = XcfColorMode.Grayscale,
      Version = 1,
      PixelData = new byte[4 * 4 * 2] // GrayA
    });
    var result = XcfReader.FromBytes(xcf);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.ColorMode, Is.EqualTo(XcfColorMode.Grayscale));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var xcf = XcfWriter.ToBytes(new XcfFile {
      Width = 2,
      Height = 2,
      ColorMode = XcfColorMode.Rgb,
      Version = 1,
      PixelData = new byte[2 * 2 * 4]
    });
    using var ms = new MemoryStream(xcf);
    var result = XcfReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }
}
