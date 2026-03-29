using System;
using System.Buffers.Binary;
using FileFormat.Dng;

namespace FileFormat.Dng.Tests;

[TestFixture]
public sealed class DngWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => DngWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TiffHeaderByteOrder() {
    var file = _MakeGrayscale(2, 2);
    var bytes = DngWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'I'));
    Assert.That(bytes[1], Is.EqualTo((byte)'I'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TiffMagicNumber() {
    var file = _MakeGrayscale(2, 2);
    var bytes = DngWriter.ToBytes(file);
    var magic = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));

    Assert.That(magic, Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IfdOffsetIs8() {
    var file = _MakeGrayscale(2, 2);
    var bytes = DngWriter.ToBytes(file);
    var ifdOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));

    Assert.That(ifdOffset, Is.EqualTo(8u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IfdEntryCount() {
    var file = _MakeGrayscale(2, 2);
    var bytes = DngWriter.ToBytes(file);
    var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));

    Assert.That(entryCount, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsDngVersionTag() {
    var file = _MakeGrayscale(2, 2);
    var bytes = DngWriter.ToBytes(file);

    var found = _FindTag(bytes, 50706);
    Assert.That(found, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsDngBackwardVersionTag() {
    var file = _MakeGrayscale(2, 2);
    var bytes = DngWriter.ToBytes(file);

    var found = _FindTag(bytes, 50707);
    Assert.That(found, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsUniqueCameraModelTag() {
    var file = new DngFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      Photometric = DngPhotometric.BlackIsZero,
      CameraModel = "TestCam",
      PixelData = new byte[4]
    };
    var bytes = DngWriter.ToBytes(file);

    var found = _FindTag(bytes, 50708);
    Assert.That(found, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsInIfd() {
    var file = _MakeRgb(5, 7);
    var bytes = DngWriter.ToBytes(file);

    // Read ImageWidth (tag 256) value from first IFD entry
    var widthValue = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(10 + 8)); // first entry value at offset 10+8
    Assert.That(widthValue, Is.EqualTo(5u));

    // ImageLength (tag 257) is second entry
    var heightValue = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(10 + 12 + 8));
    Assert.That(heightValue, Is.EqualTo(7u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NextIfdOffsetIsZero() {
    var file = _MakeGrayscale(2, 2);
    var bytes = DngWriter.ToBytes(file);
    var nextIfdOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8 + 2 + 12 * 12));

    Assert.That(nextIfdOffset, Is.EqualTo(0u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StripDataPresent() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new DngFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      Photometric = DngPhotometric.BlackIsZero,
      PixelData = pixelData
    };

    var bytes = DngWriter.ToBytes(file);
    var pixelStart = bytes.Length - 4;

    Assert.That(bytes[pixelStart], Is.EqualTo(0xAA));
    Assert.That(bytes[pixelStart + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[pixelStart + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[pixelStart + 3], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_Grayscale() {
    var w = 4;
    var h = 3;
    var file = new DngFile {
      Width = w,
      Height = h,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      Photometric = DngPhotometric.BlackIsZero,
      CameraModel = "",
      PixelData = new byte[w * h]
    };

    var bytes = DngWriter.ToBytes(file);
    // header(8) + IFD(2 + 12*12 + 4) + no bps external + no model external + pixels
    // CameraModel "" -> null-terminated 1 byte, fits in value field
    var expectedSize = 8 + 2 + 12 * 12 + 4 + w * h;

    Assert.That(bytes.Length, Is.EqualTo(expectedSize));
  }

  // --- helpers ---

  private static DngFile _MakeGrayscale(int w, int h) => new() {
    Width = w,
    Height = h,
    SamplesPerPixel = 1,
    BitsPerSample = 8,
    Photometric = DngPhotometric.BlackIsZero,
    PixelData = new byte[w * h]
  };

  private static DngFile _MakeRgb(int w, int h) => new() {
    Width = w,
    Height = h,
    SamplesPerPixel = 3,
    BitsPerSample = 8,
    Photometric = DngPhotometric.Rgb,
    PixelData = new byte[w * h * 3]
  };

  private static bool _FindTag(byte[] bytes, ushort tagId) {
    var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
    for (var i = 0; i < entryCount; ++i) {
      var offset = 10 + i * 12;
      var tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
      if (tag == tagId)
        return true;
    }

    return false;
  }
}
