using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Dpx;

namespace FileFormat.Dpx.Tests;

[TestFixture]
public sealed class DpxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DpxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DpxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dpx"));
    Assert.Throws<FileNotFoundException>(() => DpxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DpxReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[1024];
    Assert.Throws<InvalidDataException>(() => DpxReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[2048];
    BinaryPrimitives.WriteInt32BigEndian(bad.AsSpan(0), 0x12345678);
    Assert.Throws<InvalidDataException>(() => DpxReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidBigEndian_ParsesCorrectly() {
    var data = _BuildMinimalDpx(64, 48, 10, true);
    var result = DpxReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(64));
      Assert.That(result.Height, Is.EqualTo(48));
      Assert.That(result.BitsPerElement, Is.EqualTo(10));
      Assert.That(result.IsBigEndian, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidLittleEndian_ParsesCorrectly() {
    var data = _BuildMinimalDpx(32, 24, 10, false);
    var result = DpxReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(32));
      Assert.That(result.Height, Is.EqualTo(24));
      Assert.That(result.BitsPerElement, Is.EqualTo(10));
      Assert.That(result.IsBigEndian, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidDpx_ParsesDescriptor() {
    var data = _BuildMinimalDpx(4, 2, 10, true, DpxDescriptor.Rgb);
    var result = DpxReader.FromBytes(data);

    Assert.That(result.Descriptor, Is.EqualTo(DpxDescriptor.Rgb));
  }

  private static byte[] _BuildMinimalDpx(int width, int height, int bitsPerElement, bool isBigEndian, DpxDescriptor descriptor = DpxDescriptor.Rgb) {
    var pixelsPerRow = width;
    var pixelDataSize = pixelsPerRow * height * 4;
    var fileSize = 2048 + pixelDataSize;
    var data = new byte[fileSize];
    var span = data.AsSpan();

    // File info header — magic is always written big-endian (it's a byte signature)
    var magic = isBigEndian ? DpxHeader.MagicBigEndian : DpxHeader.MagicLittleEndian;
    BinaryPrimitives.WriteInt32BigEndian(span, magic);
    if (isBigEndian) {
      BinaryPrimitives.WriteInt32BigEndian(span[4..], 2048); // image data offset
      BinaryPrimitives.WriteInt32BigEndian(span[16..], fileSize);
    } else {
      BinaryPrimitives.WriteInt32LittleEndian(span[4..], 2048);
      BinaryPrimitives.WriteInt32LittleEndian(span[16..], fileSize);
    }

    // Version
    var version = "V2.0\0\0\0\0"u8;
    version.CopyTo(span[8..]);

    // Image info header at offset 768
    var imageInfo = span[768..];
    if (isBigEndian) {
      BinaryPrimitives.WriteInt16BigEndian(imageInfo, 0); // orientation
      BinaryPrimitives.WriteInt16BigEndian(imageInfo[2..], 1); // numElements
      BinaryPrimitives.WriteInt32BigEndian(imageInfo[4..], width);
      BinaryPrimitives.WriteInt32BigEndian(imageInfo[8..], height);
    } else {
      BinaryPrimitives.WriteInt16LittleEndian(imageInfo, 0);
      BinaryPrimitives.WriteInt16LittleEndian(imageInfo[2..], 1);
      BinaryPrimitives.WriteInt32LittleEndian(imageInfo[4..], width);
      BinaryPrimitives.WriteInt32LittleEndian(imageInfo[8..], height);
    }

    // First element descriptor at offset 780 (768 + 12)
    var elementBase = 768 + 12;
    span[elementBase + 20] = (byte)descriptor; // descriptor at offset 800
    span[elementBase + 23] = (byte)bitsPerElement; // bits per element at offset 803

    // Fill pixel data with a pattern
    for (var i = 2048; i < fileSize; i += 4)
      if (i + 4 <= fileSize)
        BinaryPrimitives.WriteInt32BigEndian(span[i..], (i - 2048) / 4);

    return data;
  }
}
