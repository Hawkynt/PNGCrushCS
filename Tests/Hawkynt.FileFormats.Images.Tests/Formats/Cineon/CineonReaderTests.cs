using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Cineon;

namespace FileFormat.Cineon.Tests;

[TestFixture]
public sealed class CineonReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CineonReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CineonReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cin"));
    Assert.Throws<FileNotFoundException>(() => CineonReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CineonReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[512];
    Assert.Throws<InvalidDataException>(() => CineonReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[1024];
    BinaryPrimitives.WriteInt32BigEndian(bad.AsSpan(0), 0x12345678);
    Assert.Throws<InvalidDataException>(() => CineonReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidCineon_ParsesCorrectly() {
    var data = _BuildMinimalCineon(64, 48, 10);
    var result = CineonReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(64));
      Assert.That(result.Height, Is.EqualTo(48));
      Assert.That(result.BitsPerSample, Is.EqualTo(10));
    });
  }

  private static byte[] _BuildMinimalCineon(int width, int height, int bitsPerSample) {
    // 3 channels x 10-bit = 30 bits per pixel packed into 32-bit words
    var pixelsPerRow = width;
    var wordsPerRow = pixelsPerRow; // one 32-bit word per pixel (3 x 10-bit + 2 unused)
    var pixelDataSize = wordsPerRow * height * 4;
    var fileSize = CineonHeader.StructSize + pixelDataSize;
    var data = new byte[fileSize];
    var span = data.AsSpan();

    BinaryPrimitives.WriteInt32BigEndian(span, CineonHeader.MagicNumber);
    BinaryPrimitives.WriteInt32BigEndian(span[4..], CineonHeader.StructSize);
    BinaryPrimitives.WriteInt32BigEndian(span[8..], CineonHeader.StructSize);
    BinaryPrimitives.WriteInt32BigEndian(span[20..], fileSize);
    span[192] = 0; // orientation
    span[193] = 1; // numElements
    span[198] = (byte)bitsPerSample;
    BinaryPrimitives.WriteInt32BigEndian(span[200..], width);
    BinaryPrimitives.WriteInt32BigEndian(span[204..], height);

    // Fill pixel data with a recognizable pattern
    for (var i = CineonHeader.StructSize; i < fileSize; i += 4)
      if (i + 4 <= fileSize)
        BinaryPrimitives.WriteInt32BigEndian(span[i..], (i - CineonHeader.StructSize) / 4);

    return data;
  }
}
