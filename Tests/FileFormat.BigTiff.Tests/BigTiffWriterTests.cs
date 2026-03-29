using System;
using System.Buffers.Binary;
using FileFormat.BigTiff;

namespace FileFormat.BigTiff.Tests;

[TestFixture]
public sealed class BigTiffWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => BigTiffWriter.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesLeByteOrder() {
    var data = _WriteGray(2, 2);
    Assert.Multiple(() => {
      Assert.That(data[0], Is.EqualTo(0x49));
      Assert.That(data[1], Is.EqualTo(0x49));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesVersion43() {
    var data = _WriteGray(2, 2);
    var version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
    Assert.That(version, Is.EqualTo(43));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesOffsetSize8() {
    var data = _WriteGray(2, 2);
    var offsetSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
    Assert.That(offsetSize, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesReservedZero() {
    var data = _WriteGray(2, 2);
    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6));
    Assert.That(reserved, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FirstIfdOffset_Is16() {
    var data = _WriteGray(2, 2);
    var ifdOffset = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8));
    Assert.That(ifdOffset, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IfdEntryCount_Is9() {
    var data = _WriteGray(2, 2);
    var entryCount = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(16));
    Assert.That(entryCount, Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NextIfdOffset_IsZero() {
    var data = _WriteGray(2, 2);
    var afterEntries = 16 + 8 + 9 * 20;
    var nextIfd = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(afterEntries));
    Assert.That(nextIfd, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gray_ContainsPixelData() {
    var pixels = new byte[] { 10, 20, 30, 40 };
    var data = BigTiffWriter.ToBytes(new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 1, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack,
      PixelData = pixels,
    });
    var tail = data[^4..];
    Assert.That(tail, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb_ContainsPixelData() {
    var pixels = new byte[12];
    pixels[0] = 0xFF;
    pixels[11] = 0xAA;
    var data = BigTiffWriter.ToBytes(new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricRgb,
      PixelData = pixels,
    });
    Assert.Multiple(() => {
      Assert.That(data[^12], Is.EqualTo(0xFF));
      Assert.That(data[^1], Is.EqualTo(0xAA));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Gray8_TotalSizeCorrect() {
    var data = _WriteGray(4, 3);
    var expectedPixels = 4 * 3;
    var headerAndIfd = 16 + 8 + 9 * 20 + 8;
    Assert.That(data.Length, Is.EqualTo(headerAndIfd + expectedPixels));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb24_TotalSizeCorrect() {
    var data = BigTiffWriter.ToBytes(new BigTiffFile {
      Width = 4, Height = 3, SamplesPerPixel = 3, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricRgb,
      PixelData = new byte[36],
    });
    var expectedPixels = 4 * 3 * 3;
    var headerAndIfd = 16 + 8 + 9 * 20 + 8;
    Assert.That(data.Length, Is.EqualTo(headerAndIfd + expectedPixels));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb24_BitsPerSampleInlineInIfd() {
    var data = BigTiffWriter.ToBytes(new BigTiffFile {
      Width = 2, Height = 2, SamplesPerPixel = 3, BitsPerSample = 8,
      PhotometricInterpretation = BigTiffFile.PhotometricRgb,
      PixelData = new byte[12],
    });
    // BitsPerSample is the 3rd IFD entry (index 2), each entry is 20 bytes, IFD starts at offset 16
    // Entry start: 16 (header) + 8 (entry count) + 2*20 (first two entries) = 64
    // Value field at entry+12: offset 76
    var bpsEntryValue = 64 + 12;
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(bpsEntryValue)), Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(bpsEntryValue + 2)), Is.EqualTo(8));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(bpsEntryValue + 4)), Is.EqualTo(8));
    });
  }

  private static byte[] _WriteGray(int w, int h) => BigTiffWriter.ToBytes(new BigTiffFile {
    Width = w, Height = h, SamplesPerPixel = 1, BitsPerSample = 8,
    PhotometricInterpretation = BigTiffFile.PhotometricMinIsBlack,
    PixelData = new byte[w * h],
  });
}
