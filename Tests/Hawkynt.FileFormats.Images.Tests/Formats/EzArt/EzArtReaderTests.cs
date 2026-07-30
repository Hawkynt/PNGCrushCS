using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.EzArt;

namespace FileFormat.EzArt.Tests;

[TestFixture]
public sealed class EzArtReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EzArtReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EzArtReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".eza"));
    Assert.Throws<FileNotFoundException>(() => EzArtReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EzArtReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => EzArtReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidEza_ParsesCorrectly() {
    var data = _BuildEza();
    var result = EzArtReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Palette, Has.Length.EqualTo(16));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesPalette() {
    var data = _BuildEza();
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 0x0777);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), 0x0700);

    var result = EzArtReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette[0], Is.EqualTo((short)0x0777));
      Assert.That(result.Palette[1], Is.EqualTo((short)0x0700));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var data = _BuildEza();
    data[32] = 0xAA;
    data[33] = 0xBB;
    data[32031] = 0xCC;

    var result = EzArtReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
      Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
      Assert.That(result.PixelData[31999], Is.EqualTo(0xCC));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidEza_ParsesCorrectly() {
    var data = _BuildEza();
    using var stream = new MemoryStream(data);
    var result = EzArtReader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Palette, Has.Length.EqualTo(16));
    });
  }

  private static byte[] _BuildEza() {
    var data = new byte[EzArtFile.FileSize];
    for (var i = 0; i < 32000; ++i)
      data[32 + i] = (byte)(i & 0xFF);

    return data;
  }
}
