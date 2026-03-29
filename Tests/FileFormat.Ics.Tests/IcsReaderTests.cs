using System;
using System.IO;
using System.Text;
using FileFormat.Ics;

namespace FileFormat.Ics.Tests;

[TestFixture]
public sealed class IcsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ics"));
    Assert.Throws<FileNotFoundException>(() => IcsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => IcsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidHeader_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("not_ics_version\t2.0\nend\n");
    Assert.Throws<InvalidDataException>(() => IcsReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale_ParsesCorrectly() {
    var pixelData = new byte[4 * 3]; // 4x3 grayscale
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var header = _BuildHeader(4, 3, 1, 8);
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = IcsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Channels, Is.EqualTo(1));
    Assert.That(result.BitsPerSample, Is.EqualTo(8));
    Assert.That(result.Compression, Is.EqualTo(IcsCompression.Uncompressed));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var pixelData = new byte[2 * 2 * 3]; // 2x2 RGB
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var header = _BuildHeader(2, 2, 3, 8);
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = IcsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Channels, Is.EqualTo(3));
    Assert.That(result.BitsPerSample, Is.EqualTo(8));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidGrayscale_ParsesCorrectly() {
    var pixelData = new byte[2 * 2]; // 2x2 grayscale
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 42 % 256);

    var header = _BuildHeader(2, 2, 1, 8);
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    using var ms = new MemoryStream(data);
    var result = IcsReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Channels, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Version_IsParsed() {
    var header = _BuildHeader(1, 1, 1, 8);
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var data = new byte[headerBytes.Length + 1];
    Array.Copy(headerBytes, data, headerBytes.Length);

    var result = IcsReader.FromBytes(data);

    Assert.That(result.Version, Is.EqualTo("2.0"));
  }

  private static string _BuildHeader(int width, int height, int channels, int bits) {
    var sb = new StringBuilder();
    sb.Append("ics_version\t2.0\n");
    if (channels > 1) {
      sb.Append("layout\tparameters\t4\n");
      sb.Append("layout\torder\tbits\tx\ty\tch\n");
      sb.Append("layout\tsizes\t").Append(bits).Append('\t').Append(width).Append('\t').Append(height).Append('\t').Append(channels).Append('\n');
    } else {
      sb.Append("layout\tparameters\t3\n");
      sb.Append("layout\torder\tbits\tx\ty\n");
      sb.Append("layout\tsizes\t").Append(bits).Append('\t').Append(width).Append('\t').Append(height).Append('\n');
    }
    sb.Append("layout\tsignificant_bits\t").Append(bits).Append('\n');
    sb.Append("representation\tformat\tinteger\n");
    sb.Append("representation\tcompression\tuncompressed\n");
    sb.Append("representation\tbyte_order\t1 2 3 4\n");
    sb.Append("end\n");
    return sb.ToString();
  }
}
