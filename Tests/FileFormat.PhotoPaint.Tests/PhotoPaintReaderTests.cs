using System;
using System.IO;
using FileFormat.PhotoPaint;

namespace FileFormat.PhotoPaint.Tests;

[TestFixture]
public sealed class PhotoPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PhotoPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PhotoPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpt"));
    Assert.Throws<FileNotFoundException>(() => PhotoPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PhotoPaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => PhotoPaintReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[PhotoPaintFile.HeaderSize];
    bad[0] = (byte)'N';
    bad[1] = (byte)'O';
    bad[2] = (byte)'P';
    bad[3] = (byte)'E';
    Assert.Throws<InvalidDataException>(() => PhotoPaintReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb24_ParsesCorrectly() {
    const int width = 4;
    const int height = 3;
    var data = _BuildMinimalCpt(width, height);
    var result = PhotoPaintReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.PixelData.Length, Is.EqualTo(width * height * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelData_ReadCorrectly() {
    const int width = 2;
    const int height = 1;
    var data = _BuildMinimalCpt(width, height);

    data[PhotoPaintFile.HeaderSize] = 0xAA;
    data[PhotoPaintFile.HeaderSize + 1] = 0xBB;
    data[PhotoPaintFile.HeaderSize + 2] = 0xCC;
    data[PhotoPaintFile.HeaderSize + 3] = 0x11;
    data[PhotoPaintFile.HeaderSize + 4] = 0x22;
    data[PhotoPaintFile.HeaderSize + 5] = 0x33;

    var result = PhotoPaintReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
    Assert.That(result.PixelData[2], Is.EqualTo(0xCC));
    Assert.That(result.PixelData[3], Is.EqualTo(0x11));
    Assert.That(result.PixelData[4], Is.EqualTo(0x22));
    Assert.That(result.PixelData[5], Is.EqualTo(0x33));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesCorrectly() {
    const int width = 3;
    const int height = 2;
    var data = _BuildMinimalCpt(width, height);

    using var ms = new MemoryStream(data);
    var result = PhotoPaintReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroDimension_ThrowsInvalidDataException() {
    var data = _BuildMinimalCpt(1, 1);
    data[8] = 0;
    data[9] = 0;
    data[10] = 0;
    data[11] = 0;
    Assert.Throws<InvalidDataException>(() => PhotoPaintReader.FromBytes(data));
  }

  private static byte[] _BuildMinimalCpt(int width, int height) {
    var pixelBytes = width * height * 3;
    var data = new byte[PhotoPaintFile.HeaderSize + pixelBytes];

    data[0] = PhotoPaintFile.Magic[0];
    data[1] = PhotoPaintFile.Magic[1];
    data[2] = PhotoPaintFile.Magic[2];
    data[3] = PhotoPaintFile.Magic[3];

    data[4] = (byte)(PhotoPaintFile.Version & 0xFF);
    data[5] = (byte)((PhotoPaintFile.Version >> 8) & 0xFF);

    data[8] = (byte)(width & 0xFF);
    data[9] = (byte)((width >> 8) & 0xFF);
    data[10] = (byte)((width >> 16) & 0xFF);
    data[11] = (byte)((width >> 24) & 0xFF);

    data[12] = (byte)(height & 0xFF);
    data[13] = (byte)((height >> 8) & 0xFF);
    data[14] = (byte)((height >> 16) & 0xFF);
    data[15] = (byte)((height >> 24) & 0xFF);

    data[16] = (byte)(PhotoPaintFile.BitDepth & 0xFF);
    data[17] = (byte)((PhotoPaintFile.BitDepth >> 8) & 0xFF);

    return data;
  }
}
