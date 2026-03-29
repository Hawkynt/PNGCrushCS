using System;
using System.Buffers.Binary;
using FileFormat.SymbianMbm;

namespace FileFormat.SymbianMbm.Tests;

[TestFixture]
public sealed class SymbianMbmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SymbianMbmWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytes_AreCorrect() {
    var file = new SymbianMbmFile {
      Bitmaps = []
    };

    var bytes = SymbianMbmWriter.ToBytes(file);
    var span = bytes.AsSpan();

    var uid1 = BinaryPrimitives.ReadUInt32LittleEndian(span);
    var uid2 = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
    var uid3 = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);

    Assert.That(uid1, Is.EqualTo(SymbianMbmFile.Uid1));
    Assert.That(uid2, Is.EqualTo(SymbianMbmFile.Uid2));
    Assert.That(uid3, Is.EqualTo(SymbianMbmFile.Uid3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicRawBytes_MatchSpec() {
    var file = new SymbianMbmFile {
      Bitmaps = []
    };

    var bytes = SymbianMbmWriter.ToBytes(file);

    // UID1: 37 00 00 10 (LE for 0x10000037)
    Assert.That(bytes[0], Is.EqualTo(0x37));
    Assert.That(bytes[1], Is.EqualTo(0x00));
    Assert.That(bytes[2], Is.EqualTo(0x00));
    Assert.That(bytes[3], Is.EqualTo(0x10));

    // UID2: 00 00 00 10 (LE for 0x10000000)
    Assert.That(bytes[4], Is.EqualTo(0x00));
    Assert.That(bytes[5], Is.EqualTo(0x00));
    Assert.That(bytes[6], Is.EqualTo(0x00));
    Assert.That(bytes[7], Is.EqualTo(0x10));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyBitmaps_ValidSize() {
    var file = new SymbianMbmFile {
      Bitmaps = []
    };

    var bytes = SymbianMbmWriter.ToBytes(file);

    // Header (20) + trailer (4 count + 0 offsets) = 24
    Assert.That(bytes.Length, Is.EqualTo(SymbianMbmFile.HeaderSize + 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleBitmap_ValidSize() {
    var pixelData = new byte[8]; // 4x2 at 8bpp, 4-byte row alignment
    var file = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 4,
          Height = 2,
          BitsPerPixel = 8,
          DataSize = (uint)pixelData.Length,
          PixelData = pixelData,
        }
      ]
    };

    var bytes = SymbianMbmWriter.ToBytes(file);

    // Header (20) + bitmap header (40) + pixel data (8) + trailer (4 + 4) = 76
    var expected = SymbianMbmFile.HeaderSize + SymbianMbmFile.BitmapHeaderSize + pixelData.Length + 4 + 4;
    Assert.That(bytes.Length, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TrailerOffset_PointsToTrailer() {
    var file = new SymbianMbmFile {
      Bitmaps = []
    };

    var bytes = SymbianMbmWriter.ToBytes(file);
    var span = bytes.AsSpan();
    var trailerOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);

    Assert.That(trailerOffset, Is.EqualTo(SymbianMbmFile.HeaderSize));

    var bitmapCount = BinaryPrimitives.ReadUInt32LittleEndian(span[trailerOffset..]);
    Assert.That(bitmapCount, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapHeader_WritesDimensions() {
    var file = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 16,
          Height = 8,
          BitsPerPixel = 24,
          DataSize = 0,
          PixelData = [],
        }
      ]
    };

    var bytes = SymbianMbmWriter.ToBytes(file);
    var bmpSpan = bytes.AsSpan(SymbianMbmFile.HeaderSize);

    var width = BinaryPrimitives.ReadInt32LittleEndian(bmpSpan[8..]);
    var height = BinaryPrimitives.ReadInt32LittleEndian(bmpSpan[12..]);
    var bpp = BinaryPrimitives.ReadInt32LittleEndian(bmpSpan[16..]);

    Assert.That(width, Is.EqualTo(16));
    Assert.That(height, Is.EqualTo(8));
    Assert.That(bpp, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitmapHeaderLen_Is40() {
    var file = new SymbianMbmFile {
      Bitmaps = [
        new SymbianMbmBitmap {
          Width = 2,
          Height = 2,
          BitsPerPixel = 8,
          DataSize = 0,
          PixelData = [],
        }
      ]
    };

    var bytes = SymbianMbmWriter.ToBytes(file);
    var bmpSpan = bytes.AsSpan(SymbianMbmFile.HeaderSize);
    var headerLen = BinaryPrimitives.ReadUInt32LittleEndian(bmpSpan[4..]);

    Assert.That(headerLen, Is.EqualTo(40));
  }
}
