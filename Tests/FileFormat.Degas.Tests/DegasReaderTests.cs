using System;
using System.IO;
using FileFormat.Degas;

namespace FileFormat.Degas.Tests;

[TestFixture]
public sealed class DegasReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DegasReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DegasReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pi1"));
    Assert.Throws<FileNotFoundException>(() => DegasReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DegasReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => DegasReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidLowRes_ParsesCorrectly() {
    var data = _BuildUncompressedDegas(DegasResolution.Low);
    var result = DegasReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Resolution, Is.EqualTo(DegasResolution.Low));
      Assert.That(result.IsCompressed, Is.False);
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
      Assert.That(result.Palette, Has.Length.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHighRes_ParsesCorrectly() {
    var data = _BuildUncompressedDegas(DegasResolution.High);
    var result = DegasReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(640));
      Assert.That(result.Height, Is.EqualTo(400));
      Assert.That(result.Resolution, Is.EqualTo(DegasResolution.High));
      Assert.That(result.IsCompressed, Is.False);
    });
  }

  private static byte[] _BuildUncompressedDegas(DegasResolution resolution) {
    var data = new byte[34 + 32000];
    var palette = new short[16];
    for (var i = 0; i < 16; ++i)
      palette[i] = (short)(i * 0x111 & 0x777);

    var header = DegasHeader.FromPalette((short)resolution, palette);
    header.WriteTo(data.AsSpan());

    for (var i = 0; i < 32000; ++i)
      data[34 + i] = (byte)(i & 0xFF);

    return data;
  }
}
