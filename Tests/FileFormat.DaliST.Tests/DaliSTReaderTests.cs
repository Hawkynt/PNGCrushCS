using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.DaliST;

namespace FileFormat.DaliST.Tests;

[TestFixture]
public sealed class DaliSTReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DaliSTReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DaliSTReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sd0"));
    Assert.Throws<FileNotFoundException>(() => DaliSTReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DaliSTReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => DaliSTReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidLowRes_ParsesCorrectly() {
    var data = _BuildDaliST();
    var result = DaliSTReader.FromBytes(data, DaliSTResolution.Low);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Resolution, Is.EqualTo(DaliSTResolution.Low));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
      Assert.That(result.Palette, Has.Length.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMediumRes_ParsesCorrectly() {
    var data = _BuildDaliST();
    var result = DaliSTReader.FromBytes(data, DaliSTResolution.Medium);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Resolution, Is.EqualTo(DaliSTResolution.Medium));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHighRes_ParsesCorrectly() {
    var data = _BuildDaliST();
    var result = DaliSTReader.FromBytes(data, DaliSTResolution.High);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(400));
      Assert.That(result.Resolution, Is.EqualTo(DaliSTResolution.High));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DefaultResolution_IsLow() {
    var data = _BuildDaliST();
    var result = DaliSTReader.FromBytes(data);

    Assert.That(result.Resolution, Is.EqualTo(DaliSTResolution.Low));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PreservesPaletteValues() {
    var data = _BuildDaliST();
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 0x777);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), 0x700);

    var result = DaliSTReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette[0], Is.EqualTo(0x777));
      Assert.That(result.Palette[1], Is.EqualTo(0x700));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PreservesPixelData() {
    var data = _BuildDaliST();
    for (var i = 0; i < 32000; ++i)
      data[32 + i] = (byte)(i & 0xFF);

    var result = DaliSTReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[255], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildDaliST();
    using var ms = new MemoryStream(data);
    var result = DaliSTReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
    });
  }

  private static byte[] _BuildDaliST() {
    var data = new byte[DaliSTFile.ExpectedFileSize];
    for (var i = 0; i < 16; ++i)
      BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(i * 2), (short)(i * 0x111 & 0x777));

    for (var i = 0; i < 32000; ++i)
      data[32 + i] = (byte)(i & 0xFF);

    return data;
  }
}
