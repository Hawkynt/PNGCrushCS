using System;
using System.Buffers.Binary;
using FileFormat.Clp;

namespace FileFormat.Clp.Tests;

[TestFixture]
public sealed class ClpWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithFileId() {
    var file = new ClpFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 24,
      PixelData = new byte[((2 * 24 + 31) / 32) * 4 * 2]
    };

    var bytes = ClpWriter.ToBytes(file);
    var fileId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0));

    Assert.That(fileId, Is.EqualTo(ClpHeader.FileIdValue));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FormatCountIsOne() {
    var file = new ClpFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 24,
      PixelData = new byte[((2 * 24 + 31) / 32) * 4 * 2]
    };

    var bytes = ClpWriter.ToBytes(file);
    var formatCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));

    Assert.That(formatCount, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DibDataWritten_ContainsBitmapInfoHeader() {
    var file = new ClpFile {
      Width = 4,
      Height = 3,
      BitsPerPixel = 24,
      PixelData = new byte[((4 * 24 + 31) / 32) * 4 * 3]
    };

    var bytes = ClpWriter.ToBytes(file);

    // Find the BITMAPINFOHEADER by reading the data offset from the format directory
    var dataOffset = BitConverter.ToUInt32(bytes, ClpHeader.StructSize + 2 + 4);
    var biSize = BitConverter.ToInt32(bytes, (int)dataOffset);

    Assert.That(biSize, Is.EqualTo(40)); // BITMAPINFOHEADER size
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsCorrect() {
    var file = new ClpFile {
      Width = 100,
      Height = 50,
      BitsPerPixel = 24,
      PixelData = new byte[((100 * 24 + 31) / 32) * 4 * 50]
    };

    var bytes = ClpWriter.ToBytes(file);

    var dataOffset = BitConverter.ToUInt32(bytes, ClpHeader.StructSize + 2 + 4);
    var width = BitConverter.ToInt32(bytes, (int)dataOffset + 4);
    var height = BitConverter.ToInt32(bytes, (int)dataOffset + 8);

    Assert.That(width, Is.EqualTo(100));
    Assert.That(height, Is.EqualTo(50));
  }
}
