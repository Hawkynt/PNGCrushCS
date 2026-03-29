using System;
using System.Buffers.Binary;
using FileFormat.CameraRaw;

namespace FileFormat.CameraRaw.Tests;

[TestFixture]
public sealed class CameraRawWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => CameraRawWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TiffHeaderByteOrder() {
    var file = new CameraRawFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = CameraRawWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'I'));
    Assert.That(bytes[1], Is.EqualTo((byte)'I'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TiffMagicNumber() {
    var file = new CameraRawFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = CameraRawWriter.ToBytes(file);
    var magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));

    Assert.That(magic, Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IfdOffsetIs8() {
    var file = new CameraRawFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = CameraRawWriter.ToBytes(file);
    var ifdOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));

    Assert.That(ifdOffset, Is.EqualTo(8u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IfdEntryCount() {
    var file = new CameraRawFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = CameraRawWriter.ToBytes(file);
    var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));

    Assert.That(entryCount, Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NextIfdOffsetIsZero() {
    var file = new CameraRawFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = CameraRawWriter.ToBytes(file);
    var nextIfdOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8 + 2 + 9 * 12));

    Assert.That(nextIfdOffset, Is.EqualTo(0u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize() {
    var w = 4;
    var h = 3;
    var file = new CameraRawFile {
      Width = w,
      Height = h,
      PixelData = new byte[w * h * 3]
    };

    var bytes = CameraRawWriter.ToBytes(file);
    // header(8) + IFD(2 + 9*12 + 4) + bpsExternal(3*2=6) + pixels(36)
    var expectedSize = 8 + 2 + 9 * 12 + 4 + 6 + w * h * 3;

    Assert.That(bytes.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StripDataPresent() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
    var file = new CameraRawFile {
      Width = 2,
      Height = 1,
      PixelData = pixelData
    };

    var bytes = CameraRawWriter.ToBytes(file);
    var pixelStart = bytes.Length - 6;

    Assert.That(bytes[pixelStart], Is.EqualTo(0xAA));
    Assert.That(bytes[pixelStart + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[pixelStart + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[pixelStart + 3], Is.EqualTo(0xDD));
    Assert.That(bytes[pixelStart + 4], Is.EqualTo(0xEE));
    Assert.That(bytes[pixelStart + 5], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsImageWidthTag() {
    var file = new CameraRawFile {
      Width = 10,
      Height = 5,
      PixelData = new byte[10 * 5 * 3]
    };

    var bytes = CameraRawWriter.ToBytes(file);
    // First IFD entry at offset 10 should be ImageWidth (tag 256)
    var tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10));
    var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(18));

    Assert.That(tag, Is.EqualTo(256));
    Assert.That(value, Is.EqualTo(10u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsCompressionTag_Uncompressed() {
    var file = new CameraRawFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = CameraRawWriter.ToBytes(file);
    // Compression tag (259) is the 4th entry (index 3), at offset 10 + 3*12 = 46
    var tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(46));
    var value = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(54));

    Assert.That(tag, Is.EqualTo(259));
    Assert.That(value, Is.EqualTo(1));
  }
}
