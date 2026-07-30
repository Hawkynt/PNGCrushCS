using System;
using System.IO;
using FileFormat.MsxScreen8;

namespace FileFormat.MsxScreen8.Tests;

[TestFixture]
public sealed class MsxScreen8ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen8Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen8Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sc8"));
    Assert.Throws<FileNotFoundException>(() => MsxScreen8Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen8Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => MsxScreen8Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRaw_ParsesCorrectly() {
    var data = new byte[MsxScreen8File.PixelDataSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var result = MsxScreen8Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(212));
    Assert.That(result.HasBsaveHeader, Is.False);
    Assert.That(result.PixelData.Length, Is.EqualTo(MsxScreen8File.PixelDataSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithBsaveHeader_DetectsHeader() {
    var raw = new byte[MsxScreen8File.PixelDataSize];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = (byte)(i % 256);

    var data = new byte[MsxScreen8File.BsaveHeaderSize + MsxScreen8File.PixelDataSize];
    data[0] = MsxScreen8File.BsaveMagic;
    data[1] = 0x00;
    data[2] = 0x00;
    data[3] = 0xFF;
    data[4] = 0xD3;
    data[5] = 0x00;
    data[6] = 0x00;
    Array.Copy(raw, 0, data, MsxScreen8File.BsaveHeaderSize, raw.Length);

    var result = MsxScreen8Reader.FromBytes(data);

    Assert.That(result.HasBsaveHeader, Is.True);
    Assert.That(result.PixelData, Is.EqualTo(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var data = new byte[MsxScreen8File.PixelDataSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 7 % 256);

    var result = MsxScreen8Reader.FromBytes(data);

    for (var i = 0; i < MsxScreen8File.PixelDataSize; ++i)
      Assert.That(result.PixelData[i], Is.EqualTo((byte)(i * 7 % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[MsxScreen8File.PixelDataSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    using var stream = new MemoryStream(data);
    var result = MsxScreen8Reader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(212));
  }
}
