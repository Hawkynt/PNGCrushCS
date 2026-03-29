using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Pdn;

namespace FileFormat.Pdn.Tests;

[TestFixture]
public sealed class PdnReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PdnReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PdnReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdn"));
    Assert.Throws<FileNotFoundException>(() => PdnReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PdnReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => PdnReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[16];
    data[0] = (byte)'X';
    data[1] = (byte)'Y';
    data[2] = (byte)'Z';
    data[3] = (byte)'W';
    Assert.Throws<InvalidDataException>(() => PdnReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesCorrectly() {
    const int width = 2;
    const int height = 2;
    var pixelData = new byte[width * height * 4];
    pixelData[0] = 0xFF; // B
    pixelData[1] = 0x80; // G
    pixelData[2] = 0x40; // R
    pixelData[3] = 0xFF; // A

    var bytes = PdnWriter.ToBytes(new PdnFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    });

    var result = PdnReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.Version, Is.EqualTo(3));
    Assert.That(result.PixelData.Length, Is.EqualTo(width * height * 4));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0x80));
    Assert.That(result.PixelData[2], Is.EqualTo(0x40));
    Assert.That(result.PixelData[3], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    const int width = 3;
    const int height = 2;
    var pixelData = new byte[width * height * 4];
    pixelData[0] = 0xAB;

    var bytes = PdnWriter.ToBytes(new PdnFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    });

    using var ms = new MemoryStream(bytes);
    var result = PdnReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(width));
    Assert.That(result.Height, Is.EqualTo(height));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DecompressedSizeMismatch_ThrowsInvalidDataException() {
    // Build a valid header for 2x2 but compress wrong amount of data
    using var output = new MemoryStream();
    var header = new byte[16];
    header[0] = (byte)'P';
    header[1] = (byte)'D';
    header[2] = (byte)'N';
    header[3] = (byte)'3';
    // version = 3
    header[4] = 3;
    header[5] = 0;
    // reserved = 0
    header[6] = 0;
    header[7] = 0;
    // width = 2
    header[8] = 2;
    header[9] = 0;
    header[10] = 0;
    header[11] = 0;
    // height = 2
    header[12] = 2;
    header[13] = 0;
    header[14] = 0;
    header[15] = 0;
    output.Write(header);

    // Compress only 8 bytes instead of 16
    using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
      gzip.Write(new byte[8]);

    Assert.Throws<InvalidDataException>(() => PdnReader.FromBytes(output.ToArray()));
  }
}
