using System;
using System.IO;
using FileFormat.Avif;

namespace FileFormat.Avif.Tests;

[TestFixture]
public sealed class AvifReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AvifReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AvifReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".avif"));
    Assert.Throws<FileNotFoundException>(() => AvifReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AvifReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => AvifReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidBrand_ThrowsInvalidDataException() {
    var data = _BuildMinimalFtypBox("heic");
    Assert.Throws<InvalidDataException>(() => AvifReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingFtyp_ThrowsInvalidDataException() {
    var data = IsoBmffBox.BuildBox(IsoBmffBox.Mdat, new byte[10]);
    Assert.Throws<InvalidDataException>(() => AvifReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidAvifBrand() {
    var data = _BuildValidAvifBytes(2, 1);
    var result = AvifReader.FromBytes(data);
    Assert.That(result.Brand, Is.EqualTo("avif"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AvisBrandAccepted() {
    var data = _BuildMinimalFtypBox("avis");
    var result = AvifReader.FromBytes(data);
    Assert.That(result.Brand, Is.EqualTo("avis"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DimensionsFromIspe() {
    var data = _BuildValidAvifBytes(320, 240);
    var result = AvifReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData() {
    var data = _BuildValidAvifBytes(4, 3);
    using var ms = new MemoryStream(data);
    var result = AvifReader.FromStream(ms);
    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Brand, Is.EqualTo("avif"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataExtractedFromMdat() {
    var width = 2;
    var height = 1;
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 42 % 256);

    var data = _BuildValidAvifBytesWithPixels(width, height, pixels);
    var result = AvifReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(pixels.Length));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  private static byte[] _BuildMinimalFtypBox(string brand) {
    var payload = new byte[12];
    payload[0] = (byte)brand[0];
    payload[1] = (byte)brand[1];
    payload[2] = (byte)brand[2];
    payload[3] = (byte)brand[3];
    // minor_version = 0 (bytes 4-7)
    payload[8] = (byte)brand[0];
    payload[9] = (byte)brand[1];
    payload[10] = (byte)brand[2];
    payload[11] = (byte)brand[3];
    return IsoBmffBox.BuildBox(IsoBmffBox.Ftyp, payload);
  }

  private static byte[] _BuildValidAvifBytes(int width, int height) {
    var file = new AvifFile {
      Width = width,
      Height = height,
      PixelData = new byte[width * height * 3],
      RawImageData = new byte[width * height * 3],
    };
    return AvifWriter.ToBytes(file);
  }

  private static byte[] _BuildValidAvifBytesWithPixels(int width, int height, byte[] pixels) {
    var file = new AvifFile {
      Width = width,
      Height = height,
      PixelData = pixels,
      RawImageData = pixels,
    };
    return AvifWriter.ToBytes(file);
  }
}
