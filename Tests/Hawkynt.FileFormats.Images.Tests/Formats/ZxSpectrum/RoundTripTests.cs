using System;
using FileFormat.ZxSpectrum;

namespace FileFormat.ZxSpectrum.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new ZxSpectrumFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(original);
    var restored = ZxSpectrumReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KnownBitmapPattern() {
    var bitmap = new byte[6144];
    for (var i = 0; i < bitmap.Length; ++i)
      bitmap[i] = (byte)(i * 7 % 256);

    var attributes = new byte[768];
    for (var i = 0; i < attributes.Length; ++i)
      attributes[i] = (byte)(i * 13 % 256);

    var original = new ZxSpectrumFile {
      BitmapData = bitmap,
      AttributeData = attributes,
      BorderColor = 3
    };

    var bytes = ZxSpectrumWriter.ToBytes(original);
    var restored = ZxSpectrumReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllOnes() {
    var bitmap = new byte[6144];
    Array.Fill(bitmap, (byte)0xFF);

    var attributes = new byte[768];
    Array.Fill(attributes, (byte)0xFF);

    var original = new ZxSpectrumFile {
      BitmapData = bitmap,
      AttributeData = attributes
    };

    var bytes = ZxSpectrumWriter.ToBytes(original);
    var restored = ZxSpectrumReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.AttributeData, Is.EqualTo(original.AttributeData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_InterleaveVerification_Row1_CorrectMemoryOffset() {
    // Row 1: third=0, characterRow=0, pixelLine=1
    // Interleaved address = 0*2048 + 1*256 + 0*32 = 256
    var bitmap = new byte[6144];
    var row1LinearOffset = 1 * 32; // linear row 1, byte 0
    bitmap[row1LinearOffset] = 0xBE;
    bitmap[row1LinearOffset + 1] = 0xEF;

    var original = new ZxSpectrumFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(original);

    // Verify the byte is at the correct interleaved offset
    Assert.That(bytes[256], Is.EqualTo(0xBE));
    Assert.That(bytes[257], Is.EqualTo(0xEF));

    // Verify it's NOT at the linear offset (offset 32 is row 8 in linear order)
    Assert.That(bytes[32], Is.Not.EqualTo(0xBE));

    // Read back and verify
    var restored = ZxSpectrumReader.FromBytes(bytes);
    Assert.That(restored.BitmapData[row1LinearOffset], Is.EqualTo(0xBE));
    Assert.That(restored.BitmapData[row1LinearOffset + 1], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_InterleaveVerification_Row65_CorrectMemoryOffset() {
    // Row 65: third=1, characterRow=0, pixelLine=1
    // Interleaved address = 1*2048 + 1*256 + 0*32 = 2304
    var bitmap = new byte[6144];
    var row65LinearOffset = 65 * 32;
    bitmap[row65LinearOffset] = 0xDE;

    var original = new ZxSpectrumFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(original);

    Assert.That(bytes[2304], Is.EqualTo(0xDE));

    var restored = ZxSpectrumReader.FromBytes(bytes);
    Assert.That(restored.BitmapData[row65LinearOffset], Is.EqualTo(0xDE));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_InterleaveVerification_Row191_LastRow() {
    // Row 191: third=2, characterRow=7, pixelLine=7
    // Interleaved address = 2*2048 + 7*256 + 7*32 = 4096 + 1792 + 224 = 6112
    var bitmap = new byte[6144];
    var row191LinearOffset = 191 * 32;
    bitmap[row191LinearOffset] = 0x42;

    var original = new ZxSpectrumFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(original);

    Assert.That(bytes[6112], Is.EqualTo(0x42));

    var restored = ZxSpectrumReader.FromBytes(bytes);
    Assert.That(restored.BitmapData[row191LinearOffset], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixelPerRow_AllRows() {
    var bitmap = new byte[6144];
    for (var row = 0; row < 192; ++row)
      bitmap[row * 32] = (byte)(row % 256);

    var original = new ZxSpectrumFile {
      BitmapData = bitmap,
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(original);
    var restored = ZxSpectrumReader.FromBytes(bytes);

    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new ZxSpectrumFile {
      BitmapData = new byte[6144],
      AttributeData = new byte[768]
    };

    var bytes = ZxSpectrumWriter.ToBytes(original);
    var restored = ZxSpectrumReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(192));
  }
}
