using System;
using System.Buffers.Binary;
using FileFormat.JpegXr;

namespace FileFormat.JpegXr.Tests;

[TestFixture]
public sealed class JpegXrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => JpegXrWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ByteOrderIsII() {
    var file = new JpegXrFile {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      PixelData = new byte[4]
    };

    var bytes = JpegXrWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'I'));
    Assert.That(bytes[1], Is.EqualTo((byte)'I'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicIsBC01() {
    var file = new JpegXrFile {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      PixelData = new byte[4]
    };

    var bytes = JpegXrWriter.ToBytes(file);
    var magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));

    Assert.That(magic, Is.EqualTo(0xBC01));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IfdOffsetIs8() {
    var file = new JpegXrFile {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      PixelData = new byte[4]
    };

    var bytes = JpegXrWriter.ToBytes(file);
    var ifdOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));

    Assert.That(ifdOffset, Is.EqualTo(8u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IfdEntryCount() {
    var file = new JpegXrFile {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      PixelData = new byte[4]
    };

    var bytes = JpegXrWriter.ToBytes(file);
    var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));

    Assert.That(entryCount, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WidthTagPresent() {
    var file = new JpegXrFile {
      Width = 42,
      Height = 2,
      ComponentCount = 1,
      PixelData = new byte[42 * 2]
    };

    var bytes = JpegXrWriter.ToBytes(file);
    var found = _FindTagValue(bytes, 0xBC80);

    Assert.That(found, Is.EqualTo(42u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeightTagPresent() {
    var file = new JpegXrFile {
      Width = 2,
      Height = 37,
      ComponentCount = 1,
      PixelData = new byte[2 * 37]
    };

    var bytes = JpegXrWriter.ToBytes(file);
    var found = _FindTagValue(bytes, 0xBC81);

    Assert.That(found, Is.EqualTo(37u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelFormatTag_Grayscale() {
    var file = new JpegXrFile {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      PixelData = new byte[4]
    };

    var bytes = JpegXrWriter.ToBytes(file);
    var pfByte = _FindPixelFormatByte(bytes);

    Assert.That(pfByte, Is.EqualTo(0x08));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelFormatTag_Rgb() {
    var file = new JpegXrFile {
      Width = 2,
      Height = 2,
      ComponentCount = 3,
      PixelData = new byte[12]
    };

    var bytes = JpegXrWriter.ToBytes(file);
    var pfByte = _FindPixelFormatByte(bytes);

    Assert.That(pfByte, Is.EqualTo(0x0C));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ImageDataPresent() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new JpegXrFile {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegXrWriter.ToBytes(file);

    // Image data section starts after the header (8) + IFD (2 + 5*12 + 4 = 66) = offset 74
    var imageOffset = _FindTagValue(bytes, 0xBCE0);
    var imageByteCount = _FindTagValue(bytes, 0xBCE1);

    Assert.That(imageOffset, Is.GreaterThanOrEqualTo(74u));
    Assert.That(imageByteCount, Is.GreaterThan(0u));
    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo((int)(imageOffset + imageByteCount)));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NextIfdOffsetIsZero() {
    var file = new JpegXrFile {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      PixelData = new byte[4]
    };

    var bytes = JpegXrWriter.ToBytes(file);
    // After 2 (count) + 5*12 (entries) = at offset 8 + 2 + 60 = 70
    var nextIfd = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8 + 2 + 5 * 12));

    Assert.That(nextIfd, Is.EqualTo(0u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_Grayscale() {
    var w = 4;
    var h = 3;
    var file = new JpegXrFile {
      Width = w,
      Height = h,
      ComponentCount = 1,
      PixelData = new byte[w * h]
    };

    var bytes = JpegXrWriter.ToBytes(file);
    // header(8) + IFD(2 + 5*12 + 4 = 66) = 74 minimum, plus compressed image data
    var minSize = 8 + 2 + 5 * 12 + 4;

    Assert.That(bytes.Length, Is.GreaterThan(minSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_Rgb() {
    var w = 4;
    var h = 3;
    var file = new JpegXrFile {
      Width = w,
      Height = h,
      ComponentCount = 3,
      PixelData = new byte[w * h * 3]
    };

    var bytes = JpegXrWriter.ToBytes(file);
    // header(8) + IFD(2 + 5*12 + 4 = 66) = 74 minimum, plus compressed image data
    var minSize = 8 + 2 + 5 * 12 + 4;

    Assert.That(bytes.Length, Is.GreaterThan(minSize));
  }

  /// <summary>Finds a LONG-type tag value in the IFD.</summary>
  private static uint _FindTagValue(byte[] bytes, ushort targetTag) {
    var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
    var pos = 10;
    for (var i = 0; i < entryCount; ++i) {
      var tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pos));
      if (tag == targetTag)
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 8));
      pos += 12;
    }

    throw new InvalidOperationException($"Tag 0x{targetTag:X4} not found in IFD.");
  }

  /// <summary>Finds the pixel format BYTE value in the IFD.</summary>
  private static byte _FindPixelFormatByte(byte[] bytes) {
    var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
    var pos = 10;
    for (var i = 0; i < entryCount; ++i) {
      var tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pos));
      if (tag == 0xBC01)
        return bytes[pos + 8];
      pos += 12;
    }

    throw new InvalidOperationException("PixelFormat tag not found in IFD.");
  }
}
