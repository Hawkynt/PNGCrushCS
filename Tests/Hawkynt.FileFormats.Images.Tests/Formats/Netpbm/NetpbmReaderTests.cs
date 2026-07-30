using System;
using System.IO;
using System.Text;
using FileFormat.Netpbm;

namespace FileFormat.Netpbm.Tests;

[TestFixture]
public sealed class NetpbmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NetpbmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NetpbmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ppm"));
    Assert.Throws<FileNotFoundException>(() => NetpbmReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NetpbmReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => NetpbmReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = Encoding.ASCII.GetBytes("X9\n2 2\n255\n" + new string('\0', 12));
    Assert.Throws<InvalidDataException>(() => NetpbmReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidP6_ParsesCorrectly() {
    var header = "P6\n3 2\n255\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[3 * 2 * 3]; // 3x2, RGB
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = NetpbmReader.FromBytes(data);

    Assert.That(result.Format, Is.EqualTo(NetpbmFormat.PpmBinary));
    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.MaxValue, Is.EqualTo(255));
    Assert.That(result.Channels, Is.EqualTo(3));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_P5_ParsesGrayscale() {
    var header = "P5\n4 3\n255\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = NetpbmReader.FromBytes(data);

    Assert.That(result.Format, Is.EqualTo(NetpbmFormat.PgmBinary));
    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Channels, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_P1_ParsesPbmAscii() {
    var ppm = "P1\n3 2\n1 0 1\n0 1 0\n";
    var data = Encoding.ASCII.GetBytes(ppm);

    var result = NetpbmReader.FromBytes(data);

    Assert.That(result.Format, Is.EqualTo(NetpbmFormat.PbmAscii));
    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.MaxValue, Is.EqualTo(1));
    Assert.That(result.Channels, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(new byte[] { 1, 0, 1, 0, 1, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_P7_ParsesPam() {
    var header = "P7\nWIDTH 2\nHEIGHT 2\nDEPTH 4\nMAXVAL 255\nTUPLTYPE RGB_ALPHA\nENDHDR\n";
    var headerBytes = Encoding.ASCII.GetBytes(header);
    var pixelData = new byte[2 * 2 * 4]; // 2x2, RGBA
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var data = new byte[headerBytes.Length + pixelData.Length];
    Array.Copy(headerBytes, data, headerBytes.Length);
    Array.Copy(pixelData, 0, data, headerBytes.Length, pixelData.Length);

    var result = NetpbmReader.FromBytes(data);

    Assert.That(result.Format, Is.EqualTo(NetpbmFormat.Pam));
    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.MaxValue, Is.EqualTo(255));
    Assert.That(result.Channels, Is.EqualTo(4));
    Assert.That(result.TupleType, Is.EqualTo("RGB_ALPHA"));
    Assert.That(result.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnknownMagicP8_ThrowsInvalidDataException() {
    var bad = Encoding.ASCII.GetBytes("P8\n2 2\n255\n" + new string('\0', 12));
    Assert.Throws<InvalidDataException>(() => NetpbmReader.FromBytes(bad));
  }
}
