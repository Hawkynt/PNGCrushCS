using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Vips;

namespace FileFormat.Vips.Tests;

[TestFixture]
public sealed class VipsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VipsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VipsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".v"));
    Assert.Throws<FileNotFoundException>(() => VipsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VipsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[32];
    Assert.Throws<InvalidDataException>(() => VipsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[64 + 3];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 0x12345678);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 3);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20), 0);
    Assert.Throws<InvalidDataException>(() => VipsReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesDimensions() {
    var data = _BuildValidVips(2, 3, 1);

    var result = VipsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Bands, Is.EqualTo(1));
    Assert.That(result.BandFormat, Is.EqualTo(VipsBandFormat.UChar));
    Assert.That(result.PixelData.Length, Is.EqualTo(2 * 3 * 1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesDimensions() {
    var data = _BuildValidVips(4, 2, 3);

    var result = VipsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Bands, Is.EqualTo(3));
    Assert.That(result.BandFormat, Is.EqualTo(VipsBandFormat.UChar));
    Assert.That(result.PixelData.Length, Is.EqualTo(4 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_PixelDataPreserved() {
    var w = 2;
    var h = 1;
    var bands = 3;
    var data = _BuildValidVips(w, h, bands);
    data[64] = 0xAA;
    data[65] = 0xBB;
    data[66] = 0xCC;
    data[67] = 0x11;
    data[68] = 0x22;
    data[69] = 0x33;

    var result = VipsReader.FromBytes(data);

    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
    Assert.That(result.PixelData[2], Is.EqualTo(0xCC));
    Assert.That(result.PixelData[3], Is.EqualTo(0x11));
    Assert.That(result.PixelData[4], Is.EqualTo(0x22));
    Assert.That(result.PixelData[5], Is.EqualTo(0x33));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidRgb_ParsesCorrectly() {
    var data = _BuildValidVips(2, 2, 3);
    using var ms = new MemoryStream(data);

    var result = VipsReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Bands, Is.EqualTo(3));
  }

  private static byte[] _BuildValidVips(int width, int height, int bands) {
    var pixelBytes = width * height * bands;
    var data = new byte[64 + pixelBytes];
    var span = data.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span, VipsReader.MagicValue);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt32LittleEndian(span[12..], bands);
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], (int)VipsBandFormat.UChar);
    return data;
  }
}
