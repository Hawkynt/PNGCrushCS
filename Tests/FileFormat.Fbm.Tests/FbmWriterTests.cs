using System;
using System.Buffers.Binary;
using FileFormat.Fbm;

namespace FileFormat.Fbm.Tests;

[TestFixture]
public sealed class FbmWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FbmWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagic() {
    var file = new FbmFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = [0xFF]
    };

    var bytes = FbmWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'%'));
    Assert.That(bytes[1], Is.EqualTo((byte)'b'));
    Assert.That(bytes[2], Is.EqualTo((byte)'i'));
    Assert.That(bytes[3], Is.EqualTo((byte)'t'));
    Assert.That(bytes[4], Is.EqualTo((byte)'m'));
    Assert.That(bytes[5], Is.EqualTo((byte)'a'));
    Assert.That(bytes[6], Is.EqualTo((byte)'p'));
    Assert.That(bytes[7], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderIs256Bytes() {
    var file = new FbmFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = [0xFF]
    };

    var bytes = FbmWriter.ToBytes(file);

    // minimum file size: 256 header + 16 rowlen (1 pixel, padded to 16)
    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(FbmHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsCorrect() {
    var file = new FbmFile {
      Width = 32,
      Height = 16,
      Bands = 3,
      PixelData = new byte[32 * 16 * 3]
    };

    var bytes = FbmWriter.ToBytes(file);

    var cols = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(8));
    var rows = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(12));
    var bands = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));

    Assert.That(cols, Is.EqualTo(32));
    Assert.That(rows, Is.EqualTo(16));
    Assert.That(bands, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RowLenPaddedTo16() {
    // 3 cols * 1 band = 3 bytes, padded to 16
    var file = new FbmFile {
      Width = 3,
      Height = 1,
      Bands = 1,
      PixelData = [10, 20, 30]
    };

    var bytes = FbmWriter.ToBytes(file);
    var rowLen = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(28));

    Assert.That(rowLen, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RowLenAlreadyAligned() {
    // 16 cols * 1 band = 16 bytes, already aligned
    var file = new FbmFile {
      Width = 16,
      Height = 1,
      Bands = 1,
      PixelData = new byte[16]
    };

    var bytes = FbmWriter.ToBytes(file);
    var rowLen = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(28));

    Assert.That(rowLen, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitsAndPhysBitsAre8() {
    var file = new FbmFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = [0]
    };

    var bytes = FbmWriter.ToBytes(file);

    var bits = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
    var physBits = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(24));

    Assert.That(bits, Is.EqualTo(8));
    Assert.That(physBits, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AspectIs1() {
    var file = new FbmFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = [0]
    };

    var bytes = FbmWriter.ToBytes(file);
    var aspect = BinaryPrimitives.ReadDoubleBigEndian(bytes.AsSpan(40));

    Assert.That(aspect, Is.EqualTo(1.0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ClrLenIsZero() {
    var file = new FbmFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = [0]
    };

    var bytes = FbmWriter.ToBytes(file);
    var clrLen = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(36));

    Assert.That(clrLen, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    // 4 cols * 3 bands = 12 bytes per row, rowlen = 16 (padded), 2 rows => 256 + 32 = 288
    var file = new FbmFile {
      Width = 4,
      Height = 2,
      Bands = 3,
      PixelData = new byte[4 * 2 * 3]
    };

    var bytes = FbmWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(256 + 16 * 2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new FbmFile {
      Width = 3,
      Height = 1,
      Bands = 1,
      PixelData = pixels
    };

    var bytes = FbmWriter.ToBytes(file);

    Assert.That(bytes[FbmHeader.StructSize], Is.EqualTo(0xAA));
    Assert.That(bytes[FbmHeader.StructSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[FbmHeader.StructSize + 2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TitlePreserved() {
    var file = new FbmFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = [0],
      Title = "Hello FBM"
    };

    var bytes = FbmWriter.ToBytes(file);
    var title = System.Text.Encoding.ASCII.GetString(bytes, 48, 208).TrimEnd('\0');

    Assert.That(title, Is.EqualTo("Hello FBM"));
  }
}
