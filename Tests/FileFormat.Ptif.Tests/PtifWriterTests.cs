using System;
using System.Buffers.Binary;
using FileFormat.Ptif;

namespace FileFormat.Ptif.Tests;

[TestFixture]
public sealed class PtifWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => PtifWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TiffHeaderByteOrder() {
    var file = new PtifFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = new byte[4]
    };

    var bytes = PtifWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'I'));
    Assert.That(bytes[1], Is.EqualTo((byte)'I'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TiffMagicNumber() {
    var file = new PtifFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = new byte[4]
    };

    var bytes = PtifWriter.ToBytes(file);
    var magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));

    Assert.That(magic, Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IfdOffsetIs8() {
    var file = new PtifFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = new byte[4]
    };

    var bytes = PtifWriter.ToBytes(file);
    var ifdOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));

    Assert.That(ifdOffset, Is.EqualTo(8u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IfdEntryCount() {
    var file = new PtifFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = new byte[4]
    };

    var bytes = PtifWriter.ToBytes(file);
    var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));

    Assert.That(entryCount, Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NextIfdOffsetIsZero() {
    var file = new PtifFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = new byte[4]
    };

    var bytes = PtifWriter.ToBytes(file);
    // Next IFD offset is after 2 (count) + 9*12 (entries) = at offset 8 + 2 + 108 = 118
    var nextIfdOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8 + 2 + 9 * 12));

    Assert.That(nextIfdOffset, Is.EqualTo(0u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_Grayscale() {
    var w = 4;
    var h = 3;
    var file = new PtifFile {
      Width = w,
      Height = h,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = new byte[w * h]
    };

    var bytes = PtifWriter.ToBytes(file);
    // header(8) + IFD(2 + 9*12 + 4) + pixels(12)
    var expectedSize = 8 + 2 + 9 * 12 + 4 + w * h;

    Assert.That(bytes.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_Rgb() {
    var w = 4;
    var h = 3;
    var file = new PtifFile {
      Width = w,
      Height = h,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = new byte[w * h * 3]
    };

    var bytes = PtifWriter.ToBytes(file);
    // header(8) + IFD(2 + 9*12 + 4) + bpsExternal(3*2=6) + pixels(36)
    var expectedSize = 8 + 2 + 9 * 12 + 4 + 6 + w * h * 3;

    Assert.That(bytes.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPresent() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new PtifFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = pixelData
    };

    var bytes = PtifWriter.ToBytes(file);
    var pixelStart = bytes.Length - 4;

    Assert.That(bytes[pixelStart], Is.EqualTo(0xAA));
    Assert.That(bytes[pixelStart + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[pixelStart + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[pixelStart + 3], Is.EqualTo(0xDD));
  }
}
