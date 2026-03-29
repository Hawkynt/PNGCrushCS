using System;
using System.Buffers.Binary;
using FileFormat.MacPaint;

namespace FileFormat.MacPaint.Tests;

[TestFixture]
public sealed class MacPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithHeader() {
    var file = new MacPaintFile {
      Version = 2,
      BrushPatterns = new byte[304],
      PixelData = new byte[51840]
    };

    var bytes = MacPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(512));
    var version = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0, 4));
    Assert.That(version, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderIs512Bytes_FollowedByCompressedData() {
    var pixelData = new byte[51840];
    // Fill with runs of identical bytes (compressible by PackBits)
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i / 72 % 2 == 0 ? 0xFF : 0x00);

    var file = new MacPaintFile {
      Version = 0,
      BrushPatterns = new byte[304],
      PixelData = pixelData
    };

    var bytes = MacPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(512));
    // Compressed data should be smaller than raw for repetitive patterns
    Assert.That(bytes.Length, Is.LessThan(512 + 51840));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesBrushPatterns() {
    var patterns = new byte[304];
    for (var i = 0; i < patterns.Length; ++i)
      patterns[i] = (byte)(i * 3 % 256);

    var file = new MacPaintFile {
      Version = 0,
      BrushPatterns = patterns,
      PixelData = new byte[51840]
    };

    var bytes = MacPaintWriter.ToBytes(file);

    // Patterns should be at offset 4 in the header
    var writtenPatterns = bytes.AsSpan(4, 304).ToArray();
    Assert.That(writtenPatterns, Is.EqualTo(patterns));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NullBrushPatterns_UsesZeros() {
    var file = new MacPaintFile {
      Version = 0,
      BrushPatterns = null,
      PixelData = new byte[51840]
    };

    var bytes = MacPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(512));
    // Patterns area should be all zeros
    for (var i = 4; i < 308; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Byte at offset {i} should be 0");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaddingIsZeros() {
    var file = new MacPaintFile {
      Version = 0,
      BrushPatterns = new byte[304],
      PixelData = new byte[51840]
    };

    var bytes = MacPaintWriter.ToBytes(file);

    // Padding is at offset 308 for 204 bytes
    for (var i = 308; i < 512; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Padding byte at offset {i} should be 0");
  }
}
