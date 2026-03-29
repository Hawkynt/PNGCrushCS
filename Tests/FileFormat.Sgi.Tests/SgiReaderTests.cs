using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Sgi;

namespace FileFormat.Sgi.Tests;

[TestFixture]
public sealed class SgiReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SgiReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SgiReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sgi"));
    Assert.Throws<FileNotFoundException>(() => SgiReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SgiReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => SgiReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[512];
    bad[0] = 0xFF;
    bad[1] = 0xFF;
    Assert.Throws<InvalidDataException>(() => SgiReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidUncompressed_ParsesCorrectly() {
    var data = _BuildMinimalUncompressedSgi(4, 3, 3);
    var result = SgiReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.Channels, Is.EqualTo(3));
    Assert.That(result.BytesPerChannel, Is.EqualTo(1));
    Assert.That(result.Compression, Is.EqualTo(SgiCompression.None));
  }

  private static byte[] _BuildMinimalUncompressedSgi(int width, int height, int channels) {
    var scanlineSize = width;
    var pixelDataSize = scanlineSize * height * channels;
    var data = new byte[512 + pixelDataSize];

    // Magic
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 0x01DA);
    // Compression = 0 (None)
    data[2] = 0;
    // BytesPerChannel = 1
    data[3] = 1;
    // Dimension = 3
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 3);
    // XSize
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), (ushort)width);
    // YSize
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8), (ushort)height);
    // ZSize
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(10), (ushort)channels);
    // PixMin = 0
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), 0);
    // PixMax = 255
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), 255);
    // Colormap = 0
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(104), 0);

    // Fill pixel data with a pattern
    for (var i = 0; i < pixelDataSize; ++i)
      data[512 + i] = (byte)(i * 7 % 256);

    return data;
  }
}
