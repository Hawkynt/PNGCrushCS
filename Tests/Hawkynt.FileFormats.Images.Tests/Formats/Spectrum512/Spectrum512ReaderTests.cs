using System;
using System.IO;
using FileFormat.Spectrum512;

namespace FileFormat.Spectrum512.Tests;

[TestFixture]
public sealed class Spectrum512ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spu"));
    Assert.Throws<FileNotFoundException>(() => Spectrum512Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => Spectrum512Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSpu_ParsesCorrectly() {
    var data = _BuildSpu();
    var result = Spectrum512Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(199));
      Assert.That(result.Variant, Is.EqualTo(Spectrum512Variant.Uncompressed));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
      Assert.That(result.Palettes, Has.Length.EqualTo(199));
      Assert.That(result.Palettes[0], Has.Length.EqualTo(48));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidSpu_ParsesCorrectly() {
    var data = _BuildSpu();
    using var stream = new MemoryStream(data);
    var result = Spectrum512Reader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(199));
      Assert.That(result.Palettes, Has.Length.EqualTo(199));
    });
  }

  private static byte[] _BuildSpu() {
    var data = new byte[51104];

    for (var i = 0; i < 32000; ++i)
      data[i] = (byte)(i & 0xFF);

    for (var i = 32000; i < 51104; i += 2) {
      data[i] = (byte)(((i - 32000) / 2) >> 8 & 0x07);
      data[i + 1] = (byte)(((i - 32000) / 2) & 0xFF);
    }

    return data;
  }
}
