using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.HighresMedium;

namespace FileFormat.HighresMedium.Tests;

[TestFixture]
public sealed class HighresMediumReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HighresMediumReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HighresMediumReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hrm"));
    Assert.Throws<FileNotFoundException>(() => HighresMediumReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HighresMediumReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => HighresMediumReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildMinimalFile();
    var result = HighresMediumReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette1.Length, Is.EqualTo(16));
      Assert.That(result.Palette2.Length, Is.EqualTo(16));
      Assert.That(result.PixelData1.Length, Is.EqualTo(32000));
      Assert.That(result.PixelData2.Length, Is.EqualTo(32000));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesBothPalettes() {
    var data = _BuildMinimalFile();
    // Frame 1 palette entry 0
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 0x0777);
    // Frame 2 palette entry 0
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(32032), 0x0700);

    var result = HighresMediumReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette1[0], Is.EqualTo((short)0x0777));
      Assert.That(result.Palette2[0], Is.EqualTo((short)0x0700));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesBothFramePixelData() {
    var data = _BuildMinimalFile();
    data[32] = 0xAA;         // Frame 1 pixel data offset 0
    data[32032 + 32] = 0xBB; // Frame 2 pixel data offset 0

    var result = HighresMediumReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelData1[0], Is.EqualTo(0xAA));
      Assert.That(result.PixelData2[0], Is.EqualTo(0xBB));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildMinimalFile();
    using var ms = new MemoryStream(data);
    var result = HighresMediumReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Palette1.Length, Is.EqualTo(16));
      Assert.That(result.PixelData1.Length, Is.EqualTo(32000));
      Assert.That(result.Palette2.Length, Is.EqualTo(16));
      Assert.That(result.PixelData2.Length, Is.EqualTo(32000));
    });
  }

  private static byte[] _BuildMinimalFile() {
    var data = new byte[HighresMediumFile.FileSize];
    for (var i = 0; i < 32000; ++i) {
      data[32 + i] = (byte)(i * 7 % 256);
      data[32032 + 32 + i] = (byte)(i * 11 % 256);
    }

    return data;
  }
}
