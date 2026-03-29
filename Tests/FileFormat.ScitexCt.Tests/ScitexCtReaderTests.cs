using System;
using System.IO;
using System.Text;
using FileFormat.ScitexCt;

namespace FileFormat.ScitexCt.Tests;

[TestFixture]
public sealed class ScitexCtReaderTests {

  private static byte[] _BuildValidHeader(int width, int height, ScitexCtColorMode colorMode) {
    var header = new byte[ScitexCtHeader.StructSize];
    var span = header.AsSpan();
    span.Fill((byte)' ');
    Encoding.ASCII.GetBytes("CT", span[..2]);
    Encoding.ASCII.GetBytes("000080", span.Slice(2, 6));
    Encoding.ASCII.GetBytes(width.ToString("D6"), span.Slice(8, 6));
    Encoding.ASCII.GetBytes(height.ToString("D6"), span.Slice(14, 6));
    Encoding.ASCII.GetBytes(((int)colorMode).ToString("D2"), span.Slice(20, 2));
    Encoding.ASCII.GetBytes("08", span.Slice(22, 2));
    Encoding.ASCII.GetBytes("00", span.Slice(24, 2));
    Encoding.ASCII.GetBytes("000300", span.Slice(26, 6));
    Encoding.ASCII.GetBytes("000300", span.Slice(32, 6));
    return header;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ScitexCtReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ScitexCtReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sct"));
    Assert.Throws<FileNotFoundException>(() => ScitexCtReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[40];
    Assert.Throws<InvalidDataException>(() => ScitexCtReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var data = new byte[80];
    data.AsSpan().Fill((byte)' ');
    Encoding.ASCII.GetBytes("XX", data.AsSpan(0, 2));
    Encoding.ASCII.GetBytes("000080", data.AsSpan(2, 6));
    Assert.Throws<InvalidDataException>(() => ScitexCtReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var width = 4;
    var height = 2;
    var channels = 3;
    var pixelSize = width * height * channels;
    var header = _BuildValidHeader(width, height, ScitexCtColorMode.Rgb);
    var data = new byte[ScitexCtHeader.StructSize + pixelSize];
    Array.Copy(header, data, ScitexCtHeader.StructSize);
    for (var i = 0; i < pixelSize; ++i)
      data[ScitexCtHeader.StructSize + i] = (byte)(i * 7 % 256);

    var result = ScitexCtReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.ColorMode, Is.EqualTo(ScitexCtColorMode.Rgb));
    Assert.That(result.BitsPerComponent, Is.EqualTo(8));
    Assert.That(result.PixelData.Length, Is.EqualTo(pixelSize));
    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ScitexCtReader.FromStream(null!));
  }
}
