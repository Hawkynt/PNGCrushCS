using System;
using System.IO;
using System.Text;
using FileFormat.XvThumbnail;

namespace FileFormat.XvThumbnail.Tests;

[TestFixture]
public sealed class XvThumbnailReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XvThumbnailReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XvThumbnailReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xv"));
    Assert.Throws<FileNotFoundException>(() => XvThumbnailReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XvThumbnailReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => XvThumbnailReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = Encoding.ASCII.GetBytes("P6 332\n2 2 255\n\x00\x00\x00\x00");
    Assert.Throws<InvalidDataException>(() => XvThumbnailReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSmallImage_ParsesDimensions() {
    var header = Encoding.ASCII.GetBytes("P7 332\n4 3 255\n");
    var pixels = new byte[4 * 3];
    var data = new byte[header.Length + pixels.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(pixels, 0, data, header.Length, pixels.Length);

    var result = XvThumbnailReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSmallImage_ParsesPixelData() {
    var header = Encoding.ASCII.GetBytes("P7 332\n2 2 255\n");
    var pixels = new byte[] { 0xE0, 0x1C, 0x03, 0xFF };
    var data = new byte[header.Length + pixels.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(pixels, 0, data, header.Length, pixels.Length);

    var result = XvThumbnailReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(4));
    Assert.That(result.PixelData[0], Is.EqualTo(0xE0));
    Assert.That(result.PixelData[1], Is.EqualTo(0x1C));
    Assert.That(result.PixelData[2], Is.EqualTo(0x03));
    Assert.That(result.PixelData[3], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithComments_SkipsComments() {
    var header = Encoding.ASCII.GetBytes("P7 332\n#this is a comment\n#another comment\n3 2 255\n");
    var pixels = new byte[3 * 2];
    var data = new byte[header.Length + pixels.Length];
    Array.Copy(header, data, header.Length);

    var result = XvThumbnailReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var header = Encoding.ASCII.GetBytes("P7 332\n2 2 255\n");
    var pixels = new byte[] { 0xAB, 0xCD, 0xEF, 0x12 };
    var data = new byte[header.Length + pixels.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(pixels, 0, data, header.Length, pixels.Length);

    using var ms = new MemoryStream(data);
    var result = XvThumbnailReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData_NotReference() {
    var header = Encoding.ASCII.GetBytes("P7 332\n2 1 255\n");
    var pixels = new byte[] { 0xFF, 0xAA };
    var data = new byte[header.Length + pixels.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(pixels, 0, data, header.Length, pixels.Length);

    var result = XvThumbnailReader.FromBytes(data);
    data[header.Length] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoDimensionLine_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("P7 332\n");
    Assert.Throws<InvalidDataException>(() => XvThumbnailReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingMaxval_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("P7 332\n4 3\n");
    Assert.Throws<InvalidDataException>(() => XvThumbnailReader.FromBytes(data));
  }
}
