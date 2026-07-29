using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.SyntheticArts;

namespace FileFormat.SyntheticArts.Tests;

[TestFixture]
public sealed class SyntheticArtsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SyntheticArtsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SyntheticArtsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".srt"));
    Assert.Throws<FileNotFoundException>(() => SyntheticArtsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SyntheticArtsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => SyntheticArtsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildMinimalFile();
    var result = SyntheticArtsReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette.Length, Is.EqualTo(16));
      Assert.That(result.PixelData.Length, Is.EqualTo(32000));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesPalette() {
    var data = _BuildMinimalFile();
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(SyntheticArtsFile.PaletteOffset + 0), 0x0777);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(SyntheticArtsFile.PaletteOffset + 2), 0x0700);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(SyntheticArtsFile.PaletteOffset + 4), 0x0070);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(SyntheticArtsFile.PaletteOffset + 6), 0x0007);

    var result = SyntheticArtsReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette[0], Is.EqualTo((short)0x0777));
      Assert.That(result.Palette[1], Is.EqualTo((short)0x0700));
      Assert.That(result.Palette[2], Is.EqualTo((short)0x0070));
      Assert.That(result.Palette[3], Is.EqualTo((short)0x0007));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData() {
    var data = _BuildMinimalFile();
    data[0] = 0xAA;
    data[1] = 0xBB;
    data[31999] = 0xCC;

    var result = SyntheticArtsReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
      Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
      Assert.That(result.PixelData[31999], Is.EqualTo(0xCC));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildMinimalFile();
    using var ms = new MemoryStream(data);
    var result = SyntheticArtsReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Palette.Length, Is.EqualTo(16));
      Assert.That(result.PixelData.Length, Is.EqualTo(32000));
    });
  }

  private static byte[] _BuildMinimalFile() {
    var data = new byte[SyntheticArtsFile.FileSize];
    for (var i = 0; i < SyntheticArtsFile.PixelDataSize; ++i)
      data[i] = (byte)(i * 7 % 256);

    SyntheticArtsFile.Tag.CopyTo(data.AsSpan(SyntheticArtsFile.TagOffset));
    data[SyntheticArtsFile.TagOffset + 5] = 0x01;
    return data;
  }
}
