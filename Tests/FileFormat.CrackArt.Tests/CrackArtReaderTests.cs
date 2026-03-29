using System;
using System.IO;
using FileFormat.CrackArt;

namespace FileFormat.CrackArt.Tests;

[TestFixture]
public sealed class CrackArtReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CrackArtReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CrackArtReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ca1"));
    Assert.Throws<FileNotFoundException>(() => CrackArtReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CrackArtReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => CrackArtReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidLow_ParsesCorrectly() {
    var data = _BuildCrackArtData(CrackArtResolution.Low);
    var result = CrackArtReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Resolution, Is.EqualTo(CrackArtResolution.Low));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
      Assert.That(result.Palette, Has.Length.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHigh_ParsesCorrectly() {
    var data = _BuildCrackArtData(CrackArtResolution.High);
    var result = CrackArtReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(400));
      Assert.That(result.Resolution, Is.EqualTo(CrackArtResolution.High));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidResolution_ThrowsInvalidDataException() {
    var data = _BuildCrackArtData(CrackArtResolution.Low);
    data[0] = 5;
    Assert.Throws<InvalidDataException>(() => CrackArtReader.FromBytes(data));
  }

  private static byte[] _BuildCrackArtData(CrackArtResolution resolution) {
    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i & 0xFF);

    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var file = new CrackArtFile {
      Width = 320,
      Height = 200,
      Resolution = resolution,
      Palette = palette,
      PixelData = pixelData
    };

    return CrackArtWriter.ToBytes(file);
  }
}
