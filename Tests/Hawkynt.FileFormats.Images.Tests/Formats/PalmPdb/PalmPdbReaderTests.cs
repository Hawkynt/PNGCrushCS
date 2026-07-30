using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.PalmPdb;

namespace FileFormat.PalmPdb.Tests;

[TestFixture]
public sealed class PalmPdbReaderTests {

  private static byte[] BuildMinimalPdb(int width, int height, byte[]? pixelData = null) {
    var pixels = pixelData ?? new byte[width * height * 3];
    var recordDataOffset = 78 + 8;
    var total = recordDataOffset + 4 + pixels.Length;
    var data = new byte[total];
    var span = data.AsSpan();

    // Name
    Encoding.ASCII.GetBytes("Test").CopyTo(span);

    // Type "Img " at offset 60
    span[60] = (byte)'I';
    span[61] = (byte)'m';
    span[62] = (byte)'g';
    span[63] = (byte)' ';

    // Creator "View" at offset 64
    span[64] = (byte)'V';
    span[65] = (byte)'i';
    span[66] = (byte)'e';
    span[67] = (byte)'w';

    // Record count = 1 at offset 76
    BinaryPrimitives.WriteUInt16BigEndian(span[76..], 1);

    // Record entry at offset 78: offset to image record
    BinaryPrimitives.WriteUInt32BigEndian(span[78..], (uint)recordDataOffset);

    // Image record: width, height
    BinaryPrimitives.WriteUInt16BigEndian(span[recordDataOffset..], (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(span[(recordDataOffset + 2)..], (ushort)height);

    // Pixel data
    Array.Copy(pixels, 0, data, recordDataOffset + 4, pixels.Length);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PalmPdbReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PalmPdbReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdb"));
    Assert.Throws<FileNotFoundException>(() => PalmPdbReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PalmPdbReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[40];
    Assert.Throws<InvalidDataException>(() => PalmPdbReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidType_ThrowsInvalidDataException() {
    var data = BuildMinimalPdb(2, 2);
    // Corrupt the type field
    data[60] = (byte)'X';
    data[61] = (byte)'X';
    data[62] = (byte)'X';
    data[63] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => PalmPdbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesDimensions() {
    var data = BuildMinimalPdb(4, 3);

    var result = PalmPdbReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesName() {
    var data = BuildMinimalPdb(2, 2);

    var result = PalmPdbReader.FromBytes(data);

    Assert.That(result.Name, Is.EqualTo("Test"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesPixelData() {
    var pixels = new byte[2 * 1 * 3];
    pixels[0] = 0xAA;
    pixels[1] = 0xBB;
    pixels[2] = 0xCC;
    pixels[3] = 0x11;
    pixels[4] = 0x22;
    pixels[5] = 0x33;

    var data = BuildMinimalPdb(2, 1, pixels);
    var result = PalmPdbReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(6));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[3], Is.EqualTo(0x11));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_ParsesDimensions() {
    var data = BuildMinimalPdb(3, 2);
    using var ms = new MemoryStream(data);

    var result = PalmPdbReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
  }
}
