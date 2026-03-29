using System;
using System.Buffers.Binary;
using FileFormat.Cineon;

namespace FileFormat.Cineon.Tests;

[TestFixture]
public sealed class CineonWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidCineon_StartsWithMagicNumber() {
    var file = new CineonFile {
      Width = 4,
      Height = 2,
      BitsPerSample = 10,
      PixelData = new byte[4 * 2 * 4]
    };

    var bytes = CineonWriter.ToBytes(file);

    var magic = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0));
    Assert.That(magic, Is.EqualTo(CineonHeader.MagicNumber));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize_Is1024() {
    var file = new CineonFile {
      Width = 2,
      Height = 2,
      BitsPerSample = 10,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = CineonWriter.ToBytes(file);

    var dataOffset = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(dataOffset, Is.EqualTo(CineonHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DataOffset_Is1024() {
    var file = new CineonFile {
      Width = 2,
      Height = 2,
      BitsPerSample = 10,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = CineonWriter.ToBytes(file);

    var dataOffset = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(dataOffset, Is.EqualTo(1024));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions_MatchInput() {
    var file = new CineonFile {
      Width = 320,
      Height = 240,
      BitsPerSample = 10,
      PixelData = new byte[320 * 240 * 4]
    };

    var bytes = CineonWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(200)), Is.EqualTo(320));
      Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(204)), Is.EqualTo(240));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_MatchesActualLength() {
    var pixelData = new byte[64];
    var file = new CineonFile {
      Width = 4,
      Height = 4,
      BitsPerSample = 10,
      PixelData = pixelData
    };

    var bytes = CineonWriter.ToBytes(file);

    var fileSizeField = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
    Assert.That(fileSizeField, Is.EqualTo(bytes.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_BitsPerSample_StoredInHeader() {
    var file = new CineonFile {
      Width = 2,
      Height = 2,
      BitsPerSample = 10,
      PixelData = new byte[2 * 2 * 4]
    };

    var bytes = CineonWriter.ToBytes(file);

    Assert.That(bytes[198], Is.EqualTo(10));
  }
}
