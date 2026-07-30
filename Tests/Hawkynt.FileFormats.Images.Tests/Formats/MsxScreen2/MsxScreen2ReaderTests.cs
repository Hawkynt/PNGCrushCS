using System;
using System.IO;
using FileFormat.MsxScreen2;

namespace FileFormat.MsxScreen2.Tests;

[TestFixture]
public sealed class MsxScreen2ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen2Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen2Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sc2"));
    Assert.Throws<FileNotFoundException>(() => MsxScreen2Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen2Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => MsxScreen2Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRaw_ParsesCorrectly() {
    var data = new byte[MsxScreen2File.VramDataSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var result = MsxScreen2Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.HasBsaveHeader, Is.False);
    Assert.That(result.PatternGenerator.Length, Is.EqualTo(MsxScreen2File.PatternGeneratorSize));
    Assert.That(result.ColorTable.Length, Is.EqualTo(MsxScreen2File.ColorTableSize));
    Assert.That(result.PatternNameTable.Length, Is.EqualTo(MsxScreen2File.PatternNameTableSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithBsaveHeader_DetectsHeader() {
    var raw = new byte[MsxScreen2File.VramDataSize];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = (byte)(i % 256);

    var data = new byte[MsxScreen2File.BsaveHeaderSize + MsxScreen2File.VramDataSize];
    data[0] = MsxScreen2File.BsaveMagic;
    data[1] = 0x00;
    data[2] = 0x00;
    data[3] = 0xFF;
    data[4] = 0x32;
    data[5] = 0x00;
    data[6] = 0x00;
    Array.Copy(raw, 0, data, MsxScreen2File.BsaveHeaderSize, raw.Length);

    var result = MsxScreen2Reader.FromBytes(data);

    Assert.That(result.HasBsaveHeader, Is.True);
    Assert.That(result.PatternGenerator, Is.EqualTo(raw[..MsxScreen2File.PatternGeneratorSize]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PatternGeneratorDataPreserved() {
    var data = new byte[MsxScreen2File.VramDataSize];
    for (var i = 0; i < MsxScreen2File.PatternGeneratorSize; ++i)
      data[i] = (byte)(i * 3 % 256);

    var result = MsxScreen2Reader.FromBytes(data);

    for (var i = 0; i < MsxScreen2File.PatternGeneratorSize; ++i)
      Assert.That(result.PatternGenerator[i], Is.EqualTo((byte)(i * 3 % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ColorTableDataPreserved() {
    var data = new byte[MsxScreen2File.VramDataSize];
    for (var i = 0; i < MsxScreen2File.ColorTableSize; ++i)
      data[MsxScreen2File.PatternGeneratorSize + i] = (byte)(i * 5 % 256);

    var result = MsxScreen2Reader.FromBytes(data);

    for (var i = 0; i < MsxScreen2File.ColorTableSize; ++i)
      Assert.That(result.ColorTable[i], Is.EqualTo((byte)(i * 5 % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PatternNameTableDataPreserved() {
    var data = new byte[MsxScreen2File.VramDataSize];
    var nameOffset = MsxScreen2File.PatternGeneratorSize + MsxScreen2File.ColorTableSize;
    for (var i = 0; i < MsxScreen2File.PatternNameTableSize; ++i)
      data[nameOffset + i] = (byte)(i * 7 % 256);

    var result = MsxScreen2Reader.FromBytes(data);

    for (var i = 0; i < MsxScreen2File.PatternNameTableSize; ++i)
      Assert.That(result.PatternNameTable[i], Is.EqualTo((byte)(i * 7 % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRaw_ParsesCorrectly() {
    var data = new byte[MsxScreen2File.VramDataSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    using var stream = new MemoryStream(data);
    var result = MsxScreen2Reader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }
}
