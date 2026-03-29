using System;
using FileFormat.SpookySpritesFalcon;

namespace FileFormat.SpookySpritesFalcon.Tests;

[TestFixture]
public sealed class SpookySpritesFalconRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void Decompress_Empty_ReturnsZeros() {
    var compressed = new byte[] { 0 }; // end marker only
    var result = SpookySpritesFalconRleCompressor.Decompress(compressed, 4);
    Assert.That(result.Length, Is.EqualTo(8));
    Assert.That(result, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_LiteralRun() {
    var compressed = new byte[] {
      2,           // literal count = 2
      0xF8, 0x00,  // pixel 1
      0x07, 0xE0,  // pixel 2
      0            // end
    };

    var result = SpookySpritesFalconRleCompressor.Decompress(compressed, 2);

    Assert.That(result[0], Is.EqualTo(0xF8));
    Assert.That(result[1], Is.EqualTo(0x00));
    Assert.That(result[2], Is.EqualTo(0x07));
    Assert.That(result[3], Is.EqualTo(0xE0));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_RepeatRun() {
    var compressed = new byte[] {
      unchecked((byte)(sbyte)-3), // repeat 3 times
      0xAA, 0xBB,                 // pixel value
      0                           // end
    };

    var result = SpookySpritesFalconRleCompressor.Decompress(compressed, 3);

    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xAA));
    Assert.That(result[3], Is.EqualTo(0xBB));
    Assert.That(result[4], Is.EqualTo(0xAA));
    Assert.That(result[5], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSamePixel() {
    var pixelData = new byte[20];
    for (var i = 0; i < pixelData.Length; i += 2) {
      pixelData[i] = 0xDE;
      pixelData[i + 1] = 0xAD;
    }

    var compressed = SpookySpritesFalconRleCompressor.Compress(pixelData);
    var decompressed = SpookySpritesFalconRleCompressor.Decompress(compressed, 10);

    Assert.That(decompressed, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllDifferentPixels() {
    var pixelData = new byte[8];
    pixelData[0] = 0x11;
    pixelData[1] = 0x22;
    pixelData[2] = 0x33;
    pixelData[3] = 0x44;
    pixelData[4] = 0x55;
    pixelData[5] = 0x66;
    pixelData[6] = 0x77;
    pixelData[7] = 0x88;

    var compressed = SpookySpritesFalconRleCompressor.Compress(pixelData);
    var decompressed = SpookySpritesFalconRleCompressor.Decompress(compressed, 4);

    Assert.That(decompressed, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData() {
    var pixelData = new byte[16];
    // 2 same pixels, then 2 different, then 2 same again
    pixelData[0] = 0xAA; pixelData[1] = 0xBB;
    pixelData[2] = 0xAA; pixelData[3] = 0xBB;
    pixelData[4] = 0x11; pixelData[5] = 0x22;
    pixelData[6] = 0x33; pixelData[7] = 0x44;
    pixelData[8] = 0xCC; pixelData[9] = 0xDD;
    pixelData[10] = 0xCC; pixelData[11] = 0xDD;
    pixelData[12] = 0xCC; pixelData[13] = 0xDD;
    pixelData[14] = 0xEE; pixelData[15] = 0xFF;

    var compressed = SpookySpritesFalconRleCompressor.Compress(pixelData);
    var decompressed = SpookySpritesFalconRleCompressor.Decompress(compressed, 8);

    Assert.That(decompressed, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_SmallerThanInput() {
    var pixelData = new byte[200];
    for (var i = 0; i < pixelData.Length; i += 2) {
      pixelData[i] = 0xFF;
      pixelData[i + 1] = 0xFF;
    }

    var compressed = SpookySpritesFalconRleCompressor.Compress(pixelData);

    Assert.That(compressed.Length, Is.LessThan(pixelData.Length));
  }

  [Test]
  [Category("Unit")]
  public void Compress_EndsWithZeroByte() {
    var pixelData = new byte[4];
    pixelData[0] = 0x11;
    pixelData[1] = 0x22;
    pixelData[2] = 0x33;
    pixelData[3] = 0x44;

    var compressed = SpookySpritesFalconRleCompressor.Compress(pixelData);

    Assert.That(compressed[compressed.Length - 1], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeData() {
    var pixelData = new byte[640 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var compressed = SpookySpritesFalconRleCompressor.Compress(pixelData);
    var decompressed = SpookySpritesFalconRleCompressor.Decompress(compressed, 640);

    Assert.That(decompressed, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SinglePixel() {
    var pixelData = new byte[] { 0xF8, 0x00 };

    var compressed = SpookySpritesFalconRleCompressor.Compress(pixelData);
    var decompressed = SpookySpritesFalconRleCompressor.Decompress(compressed, 1);

    Assert.That(decompressed, Is.EqualTo(pixelData));
  }
}
