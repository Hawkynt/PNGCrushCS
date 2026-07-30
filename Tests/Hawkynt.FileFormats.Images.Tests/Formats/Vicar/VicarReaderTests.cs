using System;
using System.IO;
using System.Text;
using FileFormat.Vicar;

namespace FileFormat.Vicar.Tests;

[TestFixture]
public sealed class VicarReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VicarReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VicarReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vic"));
    Assert.Throws<FileNotFoundException>(() => VicarReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => VicarReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("NOTVALID=1234567890      ");
    Assert.Throws<InvalidDataException>(() => VicarReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidByte() {
    var width = 4;
    var height = 2;
    var recSize = width;
    var headerContent = $"LBLSIZE=64 FORMAT=BYTE TYPE=IMAGE ORG=BSQ NL={height} NS={width} NB=1 RECSIZE={recSize}";
    var headerBytes = Encoding.ASCII.GetBytes(headerContent);
    var header = new byte[64];
    Array.Copy(headerBytes, header, Math.Min(headerBytes.Length, 64));
    for (var i = headerBytes.Length; i < 64; ++i)
      header[i] = (byte)' ';

    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 31 % 256);

    var data = new byte[64 + pixels.Length];
    Array.Copy(header, 0, data, 0, 64);
    Array.Copy(pixels, 0, data, 64, pixels.Length);

    var result = VicarReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Bands, Is.EqualTo(1));
    Assert.That(result.PixelType, Is.EqualTo(VicarPixelType.Byte));
    Assert.That(result.Organization, Is.EqualTo(VicarOrganization.Bsq));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VicarReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var width = 2;
    var height = 1;
    var recSize = width;
    var headerContent = $"LBLSIZE=64 FORMAT=BYTE TYPE=IMAGE ORG=BSQ NL={height} NS={width} NB=1 RECSIZE={recSize}";
    var headerBytes = Encoding.ASCII.GetBytes(headerContent);
    var header = new byte[64];
    Array.Copy(headerBytes, header, Math.Min(headerBytes.Length, 64));
    for (var i = headerBytes.Length; i < 64; ++i)
      header[i] = (byte)' ';

    var pixels = new byte[] { 0xAB, 0xCD };

    var data = new byte[64 + pixels.Length];
    Array.Copy(header, 0, data, 0, 64);
    Array.Copy(pixels, 0, data, 64, pixels.Length);

    using var ms = new MemoryStream(data);
    var result = VicarReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[1], Is.EqualTo(0xCD));
  }
}
