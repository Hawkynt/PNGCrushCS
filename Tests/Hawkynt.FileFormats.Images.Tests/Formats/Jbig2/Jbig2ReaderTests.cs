using System;
using System.IO;
using FileFormat.Jbig2;

namespace FileFormat.Jbig2.Tests;

[TestFixture]
public sealed class Jbig2ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jbig2Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jbig2Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jb2"));
    Assert.Throws<FileNotFoundException>(() => Jbig2Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[] { 0x97, 0x4A, 0x42 };
    Assert.Throws<InvalidDataException>(() => Jbig2Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    Assert.Throws<InvalidDataException>(() => Jbig2Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jbig2Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesDimensions() {
    var width = 16;
    var height = 4;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = Jbig2Writer.ToBytes(original);
    var result = Jbig2Reader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_ParsesDimensions() {
    var width = 8;
    var height = 2;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = Jbig2Writer.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var result = Jbig2Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_PixelDataPreserved() {
    var width = 8;
    var height = 1;
    var pixelData = new byte[] { 0b11001100 };

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = Jbig2Writer.ToBytes(original);
    var result = Jbig2Reader.FromBytes(bytes);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_HasSegments() {
    var width = 8;
    var height = 1;
    var pixelData = new byte[] { 0x00 };

    var original = new Jbig2File {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = Jbig2Writer.ToBytes(original);
    var result = Jbig2Reader.FromBytes(bytes);

    Assert.That(result.Segments, Is.Not.Empty);
    Assert.That(result.Segments.Length, Is.GreaterThanOrEqualTo(3));
  }
}
