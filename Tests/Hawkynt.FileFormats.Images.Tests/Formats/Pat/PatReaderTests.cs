using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Pat;

namespace FileFormat.Pat.Tests;

[TestFixture]
public sealed class PatReaderTests {

  private static byte[] BuildValidPat(int width, int height, int bpp, string name, byte[]? pixelData = null) {
    var nameBytes = Encoding.UTF8.GetBytes(name);
    var headerSize = 24 + nameBytes.Length + 1;
    var pixelCount = width * height * bpp;
    var data = new byte[headerSize + pixelCount];
    var span = data.AsSpan();

    BinaryPrimitives.WriteUInt32BigEndian(span, (uint)headerSize);
    BinaryPrimitives.WriteUInt32BigEndian(span[4..], 1u);
    BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(span[12..], (uint)height);
    BinaryPrimitives.WriteUInt32BigEndian(span[16..], (uint)bpp);
    data[20] = (byte)'G';
    data[21] = (byte)'P';
    data[22] = (byte)'A';
    data[23] = (byte)'T';
    Array.Copy(nameBytes, 0, data, 24, nameBytes.Length);
    data[24 + nameBytes.Length] = 0;

    if (pixelData != null)
      Array.Copy(pixelData, 0, data, headerSize, Math.Min(pixelData.Length, pixelCount));

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PatReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PatReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pat"));
    Assert.Throws<FileNotFoundException>(() => PatReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PatReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => PatReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[25];
    var span = data.AsSpan();
    BinaryPrimitives.WriteUInt32BigEndian(span, 25u);
    BinaryPrimitives.WriteUInt32BigEndian(span[4..], 1u);
    BinaryPrimitives.WriteUInt32BigEndian(span[8..], 1u);
    BinaryPrimitives.WriteUInt32BigEndian(span[12..], 1u);
    BinaryPrimitives.WriteUInt32BigEndian(span[16..], 1u);
    data[20] = (byte)'X';
    data[21] = (byte)'Y';
    data[22] = (byte)'Z';
    data[23] = (byte)'W';
    data[24] = 0;

    Assert.Throws<InvalidDataException>(() => PatReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGrayscale() {
    var pixels = new byte[] { 0x10, 0x20, 0x30, 0x40 };
    var data = BuildValidPat(2, 2, 1, "gray", pixels);

    var result = PatReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BytesPerPixel, Is.EqualTo(1));
    Assert.That(result.Name, Is.EqualTo("gray"));
    Assert.That(result.PixelData.Length, Is.EqualTo(4));
    Assert.That(result.PixelData[0], Is.EqualTo(0x10));
    Assert.That(result.PixelData[3], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba() {
    var pixels = new byte[16];
    for (var i = 0; i < 16; ++i)
      pixels[i] = (byte)(i * 17);

    var data = BuildValidPat(2, 2, 4, "rgba", pixels);

    var result = PatReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BytesPerPixel, Is.EqualTo(4));
    Assert.That(result.Name, Is.EqualTo("rgba"));
    Assert.That(result.PixelData.Length, Is.EqualTo(16));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidBpp_ThrowsInvalidDataException() {
    var data = new byte[25];
    var span = data.AsSpan();
    BinaryPrimitives.WriteUInt32BigEndian(span, 25u);
    BinaryPrimitives.WriteUInt32BigEndian(span[4..], 1u);
    BinaryPrimitives.WriteUInt32BigEndian(span[8..], 1u);
    BinaryPrimitives.WriteUInt32BigEndian(span[12..], 1u);
    BinaryPrimitives.WriteUInt32BigEndian(span[16..], 5u); // invalid bpp
    data[20] = (byte)'G';
    data[21] = (byte)'P';
    data[22] = (byte)'A';
    data[23] = (byte)'T';
    data[24] = 0;

    Assert.Throws<InvalidDataException>(() => PatReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InsufficientPixelData_ThrowsInvalidDataException() {
    // Build header for 10x10 RGB (300 pixel bytes) but only provide 100 bytes of total file
    var nameBytes = Encoding.UTF8.GetBytes("test");
    var headerSize = 24 + nameBytes.Length + 1;
    var data = new byte[headerSize + 10]; // way too few pixel bytes
    var span = data.AsSpan();

    BinaryPrimitives.WriteUInt32BigEndian(span, (uint)headerSize);
    BinaryPrimitives.WriteUInt32BigEndian(span[4..], 1u);
    BinaryPrimitives.WriteUInt32BigEndian(span[8..], 10u);
    BinaryPrimitives.WriteUInt32BigEndian(span[12..], 10u);
    BinaryPrimitives.WriteUInt32BigEndian(span[16..], 3u);
    data[20] = (byte)'G';
    data[21] = (byte)'P';
    data[22] = (byte)'A';
    data[23] = (byte)'T';
    Array.Copy(nameBytes, 0, data, 24, nameBytes.Length);
    data[24 + nameBytes.Length] = 0;

    Assert.Throws<InvalidDataException>(() => PatReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData() {
    var data = BuildValidPat(1, 1, 3, "s", [0xAA, 0xBB, 0xCC]);

    using var ms = new MemoryStream(data);
    var result = PatReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.BytesPerPixel, Is.EqualTo(3));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
  }
}
