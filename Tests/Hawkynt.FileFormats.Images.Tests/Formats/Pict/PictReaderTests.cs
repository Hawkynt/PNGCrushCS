using System;
using System.IO;
using FileFormat.Pict;

namespace FileFormat.Pict.Tests;

[TestFixture]
public sealed class PictReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PictReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PictReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pict"));
    Assert.Throws<FileNotFoundException>(() => PictReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => PictReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidDirectBits_ParsesCorrectly() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new PictFile {
      Width = width,
      Height = height,
      BitsPerPixel = 24,
      PixelData = pixelData
    };

    var bytes = PictWriter.ToBytes(original);
    var result = PictReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
      Assert.That(result.BitsPerPixel, Is.EqualTo(24));
      Assert.That(result.PixelData.Length, Is.EqualTo(pixelData.Length));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidPackBits_ParsesCorrectly() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 4);

    var palette = new byte[4 * 3]; // 4 colors
    palette[0] = 255; palette[1] = 0; palette[2] = 0;
    palette[3] = 0; palette[4] = 255; palette[5] = 0;
    palette[6] = 0; palette[7] = 0; palette[8] = 255;
    palette[9] = 128; palette[10] = 128; palette[11] = 128;

    var original = new PictFile {
      Width = width,
      Height = height,
      BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = palette
    };

    var bytes = PictWriter.ToBytes(original);
    var result = PictReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
      Assert.That(result.BitsPerPixel, Is.EqualTo(8));
      Assert.That(result.Palette, Is.Not.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var width = 2;
    var height = 2;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new PictFile {
      Width = width,
      Height = height,
      BitsPerPixel = 24,
      PixelData = pixelData
    };

    var bytes = PictWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var result = PictReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(width));
      Assert.That(result.Height, Is.EqualTo(height));
      Assert.That(result.BitsPerPixel, Is.EqualTo(24));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PictReader.FromStream(null!));
  }
}
