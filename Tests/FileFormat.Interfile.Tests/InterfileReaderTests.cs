using System;
using System.IO;
using System.Text;
using FileFormat.Interfile;

namespace FileFormat.Interfile.Tests;

[TestFixture]
public sealed class InterfileReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterfileReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterfileReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hv"));
    Assert.Throws<FileNotFoundException>(() => InterfileReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => InterfileReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => InterfileReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("NOT_INTERFILE := something\n!END OF INTERFILE :=\n");
    Assert.Throws<InvalidDataException>(() => InterfileReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var header = _BuildHeader(4, 3, 1, "unsigned integer");
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = InterfileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.BytesPerPixel, Is.EqualTo(1));
    Assert.That(result.NumberFormat, Is.EqualTo("unsigned integer"));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var header = _BuildHeader(2, 2, 3, "unsigned integer");
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = InterfileReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BytesPerPixel, Is.EqualTo(3));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGrayscale_ParsesCorrectly() {
    var pixelData = new byte[2 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 42 % 256);

    var header = _BuildHeader(2, 2, 1, "unsigned integer");
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    using var ms = new MemoryStream(data);
    var result = InterfileReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BytesPerPixel, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
    var header = _BuildHeader(3, 2, 1, "unsigned integer");
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = InterfileReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SignedInteger_NumberFormatParsed() {
    var header = _BuildHeader(2, 2, 2, "signed integer");
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 8];
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = InterfileReader.FromBytes(data);

    Assert.That(result.NumberFormat, Is.EqualTo("signed integer"));
    Assert.That(result.BytesPerPixel, Is.EqualTo(2));
  }

  private static string _BuildHeader(int width, int height, int bytesPerPixel, string numberFormat) {
    var sb = new StringBuilder();
    sb.Append("!INTERFILE :=\n");
    sb.Append("!imaging modality := nucmed\n");
    sb.Append("!number format := ").Append(numberFormat).Append('\n');
    sb.Append("!number of bytes per pixel := ").Append(bytesPerPixel).Append('\n');
    sb.Append("!matrix size [1] := ").Append(width).Append('\n');
    sb.Append("!matrix size [2] := ").Append(height).Append('\n');
    sb.Append("!END OF INTERFILE :=\n");
    return sb.ToString();
  }
}
