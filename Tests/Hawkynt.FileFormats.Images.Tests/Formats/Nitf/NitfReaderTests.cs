using System;
using System.IO;
using FileFormat.Nitf;

namespace FileFormat.Nitf.Tests;

[TestFixture]
public sealed class NitfReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NitfReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NitfReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ntf"));
    Assert.Throws<FileNotFoundException>(() => NitfReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NitfReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => NitfReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[16];
    // "JPEG" instead of "NITF"
    data[0] = (byte)'J';
    data[1] = (byte)'P';
    data[2] = (byte)'E';
    data[3] = (byte)'G';

    Assert.Throws<InvalidDataException>(() => NitfReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidVersion_ThrowsInvalidDataException() {
    var data = new byte[16];
    data[0] = (byte)'N';
    data[1] = (byte)'I';
    data[2] = (byte)'T';
    data[3] = (byte)'F';
    data[4] = (byte)'0';
    data[5] = (byte)'1';
    data[6] = (byte)'.';
    data[7] = (byte)'0';
    data[8] = (byte)'0';

    Assert.Throws<InvalidDataException>(() => NitfReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesDimensions() {
    var file = new NitfFile {
      Width = 4,
      Height = 3,
      Mode = NitfImageMode.Grayscale,
      PixelData = new byte[4 * 3],
    };

    var bytes = NitfWriter.ToBytes(file);
    var parsed = NitfReader.FromBytes(bytes);

    Assert.That(parsed.Width, Is.EqualTo(4));
    Assert.That(parsed.Height, Is.EqualTo(3));
    Assert.That(parsed.Mode, Is.EqualTo(NitfImageMode.Grayscale));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesDimensions() {
    var file = new NitfFile {
      Width = 8,
      Height = 6,
      Mode = NitfImageMode.Rgb,
      PixelData = new byte[8 * 6 * 3],
    };

    var bytes = NitfWriter.ToBytes(file);
    var parsed = NitfReader.FromBytes(bytes);

    Assert.That(parsed.Width, Is.EqualTo(8));
    Assert.That(parsed.Height, Is.EqualTo(6));
    Assert.That(parsed.Mode, Is.EqualTo(NitfImageMode.Rgb));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var file = new NitfFile {
      Width = 4,
      Height = 3,
      Mode = NitfImageMode.Grayscale,
      PixelData = pixelData,
    };

    var bytes = NitfWriter.ToBytes(file);
    var parsed = NitfReader.FromBytes(bytes);

    Assert.That(parsed.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGrayscale_Parses() {
    var file = new NitfFile {
      Width = 2,
      Height = 2,
      Mode = NitfImageMode.Grayscale,
      PixelData = [0x10, 0x20, 0x30, 0x40],
    };

    var bytes = NitfWriter.ToBytes(file);
    using var ms = new MemoryStream(bytes);
    var parsed = NitfReader.FromStream(ms);

    Assert.That(parsed.Width, Is.EqualTo(2));
    Assert.That(parsed.Height, Is.EqualTo(2));
    Assert.That(parsed.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TitlePreserved() {
    var file = new NitfFile {
      Width = 2,
      Height = 2,
      Mode = NitfImageMode.Grayscale,
      Title = "Test Image Title",
      PixelData = new byte[4],
    };

    var bytes = NitfWriter.ToBytes(file);
    var parsed = NitfReader.FromBytes(bytes);

    Assert.That(parsed.Title, Is.EqualTo("Test Image Title"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ClassificationPreserved() {
    var file = new NitfFile {
      Width = 2,
      Height = 2,
      Mode = NitfImageMode.Grayscale,
      Classification = 'S',
      PixelData = new byte[4],
    };

    var bytes = NitfWriter.ToBytes(file);
    var parsed = NitfReader.FromBytes(bytes);

    Assert.That(parsed.Classification, Is.EqualTo('S'));
  }
}
