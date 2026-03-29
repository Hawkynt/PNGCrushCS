using System;
using FileFormat.PhotoPaint;

namespace FileFormat.PhotoPaint.Tests;

[TestFixture]
public sealed class PhotoPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PhotoPaintWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytes_AreCptNull() {
    var file = new PhotoPaintFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3],
    };

    var bytes = PhotoPaintWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'C'));
    Assert.That(bytes[1], Is.EqualTo((byte)'P'));
    Assert.That(bytes[2], Is.EqualTo((byte)'T'));
    Assert.That(bytes[3], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Version_IsOne() {
    var file = new PhotoPaintFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3],
    };

    var bytes = PhotoPaintWriter.ToBytes(file);

    var version = (ushort)(bytes[4] | (bytes[5] << 8));
    Assert.That(version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions_StoredAsLittleEndianUint32() {
    var file = new PhotoPaintFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3],
    };

    var bytes = PhotoPaintWriter.ToBytes(file);

    var width = bytes[8] | (bytes[9] << 8) | (bytes[10] << 16) | (bytes[11] << 24);
    var height = bytes[12] | (bytes[13] << 8) | (bytes[14] << 16) | (bytes[15] << 24);

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitDepth_Is24() {
    var file = new PhotoPaintFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3],
    };

    var bytes = PhotoPaintWriter.ToBytes(file);

    var bitDepth = (ushort)(bytes[16] | (bytes[17] << 8));
    Assert.That(bitDepth, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Compression_IsZero() {
    var file = new PhotoPaintFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3],
    };

    var bytes = PhotoPaintWriter.ToBytes(file);

    var compression = (ushort)(bytes[18] | (bytes[19] << 8));
    Assert.That(compression, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_IsHeaderPlusPixelData() {
    const int width = 4;
    const int height = 3;
    var file = new PhotoPaintFile {
      Width = width,
      Height = height,
      PixelData = new byte[width * height * 3],
    };

    var bytes = PhotoPaintWriter.ToBytes(file);

    var expectedSize = PhotoPaintFile.HeaderSize + width * height * 3;
    Assert.That(bytes.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelData_IsPreserved() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0x11, 0x22, 0x33 };
    var file = new PhotoPaintFile {
      Width = 2,
      Height = 1,
      PixelData = pixelData,
    };

    var bytes = PhotoPaintWriter.ToBytes(file);

    Assert.That(bytes[PhotoPaintFile.HeaderSize], Is.EqualTo(0xAA));
    Assert.That(bytes[PhotoPaintFile.HeaderSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[PhotoPaintFile.HeaderSize + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[PhotoPaintFile.HeaderSize + 3], Is.EqualTo(0x11));
    Assert.That(bytes[PhotoPaintFile.HeaderSize + 4], Is.EqualTo(0x22));
    Assert.That(bytes[PhotoPaintFile.HeaderSize + 5], Is.EqualTo(0x33));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ReservedBytes_AreZero() {
    var file = new PhotoPaintFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3],
    };

    var bytes = PhotoPaintWriter.ToBytes(file);

    Assert.That(bytes[6], Is.EqualTo(0));
    Assert.That(bytes[7], Is.EqualTo(0));
    Assert.That(bytes[20], Is.EqualTo(0));
    Assert.That(bytes[21], Is.EqualTo(0));
    Assert.That(bytes[22], Is.EqualTo(0));
    Assert.That(bytes[23], Is.EqualTo(0));
  }
}
