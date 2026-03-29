using System;
using System.IO;
using FileFormat.Wpg;

namespace FileFormat.Wpg.Tests;

[TestFixture]
public sealed class WpgReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WpgReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WpgReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wpg"));
    Assert.Throws<FileNotFoundException>(() => WpgReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WpgReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => WpgReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[WpgHeader.StructSize + 20];
    bad[0] = 0x00;
    Assert.Throws<InvalidDataException>(() => WpgReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidBitmap_ParsesCorrectly() {
    var data = _BuildMinimalWpg(4, 3, 8);
    var result = WpgReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidBitmap_ParsesCorrectly() {
    var data = _BuildMinimalWpg(2, 2, 8);
    using var stream = new MemoryStream(data);
    var result = WpgReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  private static byte[] _BuildMinimalWpg(int width, int height, int bpp) {
    var bytesPerRow = (width * bpp + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var file = new WpgFile {
      Width = width,
      Height = height,
      BitsPerPixel = bpp,
      PixelData = pixelData
    };

    return WpgWriter.ToBytes(file);
  }
}
