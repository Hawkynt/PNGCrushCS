using System;
using System.Buffers.Binary;
using FileFormat.Pvr;

namespace FileFormat.Pvr.Tests;

[TestFixture]
public sealed class PvrWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PvrWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagic() {
    var file = new PvrFile {
      Width = 4,
      Height = 4,
      Depth = 1,
      PixelFormat = PvrPixelFormat.PVRTC_4BPP_RGBA,
      Surfaces = 1,
      Faces = 1,
      MipmapCount = 1,
      CompressedData = new byte[8]
    };

    var bytes = PvrWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    Assert.That(magic, Is.EqualTo(PvrHeader.Magic));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectDimensions() {
    var file = new PvrFile {
      Width = 16,
      Height = 8,
      Depth = 1,
      PixelFormat = PvrPixelFormat.ETC1,
      Surfaces = 1,
      Faces = 1,
      MipmapCount = 1,
      CompressedData = new byte[32]
    };

    var bytes = PvrWriter.ToBytes(file);

    var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24));
    var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28));
    Assert.That(height, Is.EqualTo(8u));
    Assert.That(width, Is.EqualTo(16u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MetadataPreserved() {
    var metadata = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };
    var file = new PvrFile {
      Width = 4,
      Height = 4,
      Depth = 1,
      PixelFormat = PvrPixelFormat.PVRTC_4BPP_RGBA,
      Surfaces = 1,
      Faces = 1,
      MipmapCount = 1,
      Metadata = metadata,
      MetadataSize = metadata.Length,
      CompressedData = new byte[8]
    };

    var bytes = PvrWriter.ToBytes(file);

    var metadataSizeField = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48));
    Assert.That(metadataSizeField, Is.EqualTo(8u));

    var writtenMetadata = new byte[8];
    Array.Copy(bytes, PvrHeader.StructSize, writtenMetadata, 0, 8);
    Assert.That(writtenMetadata, Is.EqualTo(metadata));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataPreserved() {
    var compressedData = new byte[16];
    for (var i = 0; i < compressedData.Length; ++i)
      compressedData[i] = (byte)(i * 17 % 256);

    var file = new PvrFile {
      Width = 4,
      Height = 4,
      Depth = 1,
      PixelFormat = PvrPixelFormat.PVRTC_4BPP_RGBA,
      Surfaces = 1,
      Faces = 1,
      MipmapCount = 1,
      CompressedData = compressedData
    };

    var bytes = PvrWriter.ToBytes(file);

    var writtenData = new byte[16];
    Array.Copy(bytes, PvrHeader.StructSize, writtenData, 0, 16);
    Assert.That(writtenData, Is.EqualTo(compressedData));
  }
}
