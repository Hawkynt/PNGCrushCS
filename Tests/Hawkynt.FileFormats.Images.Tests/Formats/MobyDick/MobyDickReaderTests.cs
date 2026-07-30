using System;
using System.IO;
using FileFormat.MobyDick;

namespace FileFormat.MobyDick.Tests;

[TestFixture]
public sealed class MobyDickReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MobyDickReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MobyDickReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mby"));
    Assert.Throws<FileNotFoundException>(() => MobyDickReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MobyDickReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => MobyDickReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[64769];
    Assert.Throws<InvalidDataException>(() => MobyDickReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidMobyDickFile();
    var result = MobyDickReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Palette.Length, Is.EqualTo(768));
    Assert.That(result.PixelData.Length, Is.EqualTo(64000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Palette_CopiedCorrectly() {
    var data = _BuildValidMobyDickFile();
    data[0] = 0xAB;
    data[767] = 0xCD;

    var result = MobyDickReader.FromBytes(data);

    Assert.That(result.Palette[0], Is.EqualTo(0xAB));
    Assert.That(result.Palette[767], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelData_CopiedCorrectly() {
    var data = _BuildValidMobyDickFile();
    // Pixel data starts at offset 768
    data[768] = 0x12;
    data[64767] = 0x34;

    var result = MobyDickReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0x12));
    Assert.That(result.PixelData[63999], Is.EqualTo(0x34));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidMobyDickFile();
    using var ms = new MemoryStream(data);
    var result = MobyDickReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.Palette.Length, Is.EqualTo(768));
    Assert.That(result.PixelData.Length, Is.EqualTo(64000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PaletteAndPixelData_AreSeparateArrays() {
    var data = _BuildValidMobyDickFile();
    var result = MobyDickReader.FromBytes(data);

    Assert.That(result.Palette, Is.Not.SameAs(result.PixelData));
  }

  private static byte[] _BuildValidMobyDickFile() {
    var data = new byte[MobyDickFile.ExpectedFileSize];

    for (var i = 0; i < 768; ++i)
      data[i] = (byte)(i % 256);

    for (var i = 0; i < 64000; ++i)
      data[768 + i] = (byte)(i % 256);

    return data;
  }
}
