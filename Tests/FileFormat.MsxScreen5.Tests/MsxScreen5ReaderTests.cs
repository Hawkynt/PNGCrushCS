using System;
using System.IO;
using FileFormat.MsxScreen5;

namespace FileFormat.MsxScreen5.Tests;

[TestFixture]
public sealed class MsxScreen5ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen5Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen5Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sc5"));
    Assert.Throws<FileNotFoundException>(() => MsxScreen5Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxScreen5Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => MsxScreen5Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataOnly_ParsesWithoutPalette() {
    var data = new byte[MsxScreen5File.PixelDataSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var result = MsxScreen5Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(212));
    Assert.That(result.HasBsaveHeader, Is.False);
    Assert.That(result.PixelData.Length, Is.EqualTo(MsxScreen5File.PixelDataSize));
    Assert.That(result.Palette, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithPalette_ParsesPalette() {
    var data = new byte[MsxScreen5File.FullDataSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var result = MsxScreen5Reader.FromBytes(data);

    Assert.That(result.Palette, Is.Not.Null);
    Assert.That(result.Palette!.Length, Is.EqualTo(MsxScreen5File.PaletteSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithBsaveHeader_DetectsHeader() {
    var raw = new byte[MsxScreen5File.FullDataSize];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = (byte)(i % 256);

    var data = new byte[MsxScreen5File.BsaveHeaderSize + MsxScreen5File.FullDataSize];
    data[0] = MsxScreen5File.BsaveMagic;
    data[1] = 0x00;
    data[2] = 0x00;
    data[3] = 0xFF;
    data[4] = 0x69;
    data[5] = 0x00;
    data[6] = 0x00;
    Array.Copy(raw, 0, data, MsxScreen5File.BsaveHeaderSize, raw.Length);

    var result = MsxScreen5Reader.FromBytes(data);

    Assert.That(result.HasBsaveHeader, Is.True);
    Assert.That(result.PixelData.Length, Is.EqualTo(MsxScreen5File.PixelDataSize));
    Assert.That(result.Palette, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var data = new byte[MsxScreen5File.PixelDataSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 3 % 256);

    var result = MsxScreen5Reader.FromBytes(data);

    for (var i = 0; i < MsxScreen5File.PixelDataSize; ++i)
      Assert.That(result.PixelData[i], Is.EqualTo((byte)(i * 3 % 256)));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = new byte[MsxScreen5File.FullDataSize];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    using var stream = new MemoryStream(data);
    var result = MsxScreen5Reader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(212));
    Assert.That(result.Palette, Is.Not.Null);
  }
}
