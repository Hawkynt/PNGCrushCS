using System;
using System.IO;
using FileFormat.Bsave;

namespace FileFormat.Bsave.Tests;

[TestFixture]
public sealed class BsaveReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BsaveReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BsaveReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bsv"));
    Assert.Throws<FileNotFoundException>(() => BsaveReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BsaveReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => BsaveReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[BsaveHeader.StructSize + 100];
    bad[0] = 0x00; // wrong magic
    Assert.Throws<InvalidDataException>(() => BsaveReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidVga_ParsesCorrectly() {
    var data = _BuildVgaBsave();
    var result = BsaveReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Mode, Is.EqualTo(BsaveMode.Vga320x200x256));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidCga_ParsesCorrectly() {
    var data = _BuildCgaBsave();
    var result = BsaveReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Mode, Is.EqualTo(BsaveMode.Cga320x200x4));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidVga_ParsesCorrectly() {
    var data = _BuildVgaBsave();
    using var stream = new MemoryStream(data);
    var result = BsaveReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  private static byte[] _BuildVgaBsave() {
    var pixelData = new byte[64000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new BsaveFile {
      Width = 320,
      Height = 200,
      Mode = BsaveMode.Vga320x200x256,
      PixelData = pixelData
    };

    return BsaveWriter.ToBytes(file);
  }

  private static byte[] _BuildCgaBsave() {
    var pixelData = new byte[16384];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new BsaveFile {
      Width = 320,
      Height = 200,
      Mode = BsaveMode.Cga320x200x4,
      PixelData = pixelData
    };

    return BsaveWriter.ToBytes(file);
  }
}
