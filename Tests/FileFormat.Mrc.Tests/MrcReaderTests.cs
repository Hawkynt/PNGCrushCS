using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Mrc;

namespace FileFormat.Mrc.Tests;

[TestFixture]
public sealed class MrcReaderTests {

  private static byte[] _BuildValidMrc(int nx, int ny, int nz = 1, int mode = 0, byte[]? pixelData = null) {
    var bytesPerVoxel = mode switch { 0 => 1, 1 => 2, 2 => 4, 6 => 2, _ => 1 };
    var pixels = pixelData ?? new byte[nx * ny * nz * bytesPerVoxel];
    var data = new byte[MrcFile.HeaderSize + pixels.Length];
    var span = data.AsSpan();

    BinaryPrimitives.WriteInt32LittleEndian(span, nx);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], ny);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], nz);
    BinaryPrimitives.WriteInt32LittleEndian(span[12..], mode);
    BinaryPrimitives.WriteInt32LittleEndian(span[92..], 0);

    span[208] = (byte)'M';
    span[209] = (byte)'A';
    span[210] = (byte)'P';
    span[211] = (byte)' ';
    span[212] = 0x44;
    span[213] = 0x44;

    Array.Copy(pixels, 0, data, MrcFile.HeaderSize, pixels.Length);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MrcReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MrcReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mrc"));
    Assert.Throws<FileNotFoundException>(() => MrcReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MrcReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[512];
    Assert.Throws<InvalidDataException>(() => MrcReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Exactly1023Bytes_ThrowsInvalidDataException() {
    var tooSmall = new byte[1023];
    Assert.Throws<InvalidDataException>(() => MrcReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MissingMapMagic_ThrowsInvalidDataException() {
    var data = new byte[1024];
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(), 4);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 4);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 1);
    // No MAP magic at offset 208

    Assert.Throws<InvalidDataException>(() => MrcReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode0_ParsesDimensions() {
    var data = _BuildValidMrc(16, 8);

    var result = MrcReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(8));
    Assert.That(result.Sections, Is.EqualTo(1));
    Assert.That(result.Mode, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMode0_ParsesPixelData() {
    var pixels = new byte[4 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17);

    var data = _BuildValidMrc(4, 4, pixelData: pixels);

    var result = MrcReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(16));
    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(17));
    Assert.That(result.PixelData[15], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var pixels = new byte[] { 0xAB, 0xCD, 0xEF, 0x12 };
    var data = _BuildValidMrc(2, 2, pixelData: pixels);

    var result = MrcReader.FromBytes(data);
    data[MrcFile.HeaderSize] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = _BuildValidMrc(8, 4);
    using var ms = new MemoryStream(data);

    var result = MrcReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WithExtendedHeader_ParsesCorrectly() {
    var extHeader = new byte[] { 0x01, 0x02, 0x03, 0x04 };
    var pixels = new byte[2 * 2];
    var totalSize = MrcFile.HeaderSize + extHeader.Length + pixels.Length;
    var data = new byte[totalSize];
    var span = data.AsSpan();

    BinaryPrimitives.WriteInt32LittleEndian(span, 2);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], 2);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], 1);
    BinaryPrimitives.WriteInt32LittleEndian(span[12..], 0);
    BinaryPrimitives.WriteInt32LittleEndian(span[92..], extHeader.Length);
    span[208] = (byte)'M';
    span[209] = (byte)'A';
    span[210] = (byte)'P';
    span[211] = (byte)' ';
    span[212] = 0x44;

    Array.Copy(extHeader, 0, data, MrcFile.HeaderSize, extHeader.Length);
    pixels[0] = 0xAA;
    Array.Copy(pixels, 0, data, MrcFile.HeaderSize + extHeader.Length, pixels.Length);

    var result = MrcReader.FromBytes(data);

    Assert.That(result.ExtendedHeaderSize, Is.EqualTo(4));
    Assert.That(result.ExtendedHeader.Length, Is.EqualTo(4));
    Assert.That(result.ExtendedHeader[0], Is.EqualTo(0x01));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
  }
}
