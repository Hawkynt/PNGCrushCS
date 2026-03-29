using System;
using System.IO;
using System.Text;
using FileFormat.Envi;

namespace FileFormat.Envi.Tests;

[TestFixture]
public sealed class EnviReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EnviReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EnviReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hdr"));
    Assert.Throws<FileNotFoundException>(() => EnviReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => EnviReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("NOT_ENVI header data padding here!!");
    Assert.Throws<InvalidDataException>(() => EnviReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingLineBreakAfterMagic_ThrowsInvalidDataException() {
    var data = new byte[] { 0x45, 0x4E, 0x56, 0x49, 0x20 }; // "ENVI "
    Assert.Throws<InvalidDataException>(() => EnviReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale() {
    var width = 4;
    var height = 2;
    var data = _BuildEnviData(width, height, 1, 1, "bsq", null);

    var result = EnviReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Bands, Is.EqualTo(1));
    Assert.That(result.DataType, Is.EqualTo(1));
    Assert.That(result.PixelData.Length, Is.EqualTo(width * height));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb() {
    var width = 4;
    var height = 2;
    var data = _BuildEnviData(width, height, 3, 1, "bip", null);

    var result = EnviReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Bands, Is.EqualTo(3));
    Assert.That(result.Interleave, Is.EqualTo(EnviInterleave.Bip));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgbBsq() {
    var width = 3;
    var height = 2;
    var data = _BuildEnviData(width, height, 3, 1, "bsq", null);

    var result = EnviReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Bands, Is.EqualTo(3));
    Assert.That(result.Interleave, Is.EqualTo(EnviInterleave.Bsq));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EnviReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var width = 2;
    var height = 1;
    var data = _BuildEnviData(width, height, 1, 1, "bsq", null);

    using var ms = new MemoryStream(data);
    var result = EnviReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var width = 4;
    var height = 2;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 31 % 256);

    var data = _BuildEnviData(width, height, 1, 1, "bsq", pixels);

    var result = EnviReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AcceptsCrLfLineEnding() {
    var header = "ENVI\r\nsamples = 2\r\nlines = 1\r\nbands = 1\r\ndata type = 1\r\ninterleave = bsq\r\nbyte order = 0\r\nheader offset = 0\r\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixels = new byte[] { 0xAA, 0xBB };
    var data = new byte[headerBytes.Length + pixels.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixels, 0, data, headerBytes.Length, pixels.Length);

    var result = EnviReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesBilInterleave() {
    var data = _BuildEnviData(3, 2, 3, 1, "bil", null);

    var result = EnviReader.FromBytes(data);

    Assert.That(result.Interleave, Is.EqualTo(EnviInterleave.Bil));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesByteOrder() {
    var header = "ENVI\nsamples = 2\nlines = 1\nbands = 1\ndata type = 1\ninterleave = bsq\nbyte order = 1\nheader offset = 0\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixels = new byte[] { 0x00, 0x00 };
    var data = new byte[headerBytes.Length + pixels.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixels, 0, data, headerBytes.Length, pixels.Length);

    var result = EnviReader.FromBytes(data);

    Assert.That(result.ByteOrder, Is.EqualTo(1));
  }

  private static byte[] _BuildEnviData(int width, int height, int bands, int dataType, string interleave, byte[]? pixels) {
    var sb = new StringBuilder();
    sb.Append("ENVI\n");
    sb.Append($"samples = {width}\n");
    sb.Append($"lines = {height}\n");
    sb.Append($"bands = {bands}\n");
    sb.Append($"data type = {dataType}\n");
    sb.Append($"interleave = {interleave}\n");
    sb.Append("byte order = 0\n");
    sb.Append("header offset = 0\n");

    var headerBytes = Encoding.ASCII.GetBytes(sb.ToString());

    var bytesPerSample = dataType switch {
      1 => 1,
      2 => 2,
      4 => 4,
      12 => 2,
      _ => 1
    };
    var pixelCount = width * height * bands * bytesPerSample;
    if (pixels == null) {
      pixels = new byte[pixelCount];
      for (var i = 0; i < pixels.Length; ++i)
        pixels[i] = (byte)(i * 17 % 256);
    }

    var result = new byte[headerBytes.Length + pixelCount];
    Array.Copy(headerBytes, 0, result, 0, headerBytes.Length);
    Array.Copy(pixels, 0, result, headerBytes.Length, Math.Min(pixels.Length, pixelCount));

    return result;
  }
}
