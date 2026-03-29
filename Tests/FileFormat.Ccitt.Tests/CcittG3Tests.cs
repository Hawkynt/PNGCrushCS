using System;
using FileFormat.Ccitt;

namespace FileFormat.Ccitt.Tests;

[TestFixture]
public sealed class CcittG3Tests {

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_AllWhiteScanline_RoundTrips() {
    var width = 32;
    var bytesPerRow = width / 8;
    var pixelData = new byte[bytesPerRow]; // all zeros = all white

    var compressed = CcittG3Encoder.Encode(pixelData, width, 1);
    var decoded = CcittG3Decoder.Decode(compressed, width, 1);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_AllBlackScanline_RoundTrips() {
    var width = 32;
    var bytesPerRow = width / 8;
    var pixelData = new byte[bytesPerRow];
    Array.Fill(pixelData, (byte)0xFF);

    var compressed = CcittG3Encoder.Encode(pixelData, width, 1);
    var decoded = CcittG3Decoder.Decode(compressed, width, 1);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_AlternatingScanline_RoundTrips() {
    // Alternating black and white pixels: 10101010
    var width = 8;
    var pixelData = new byte[] { 0b10101010 };

    var compressed = CcittG3Encoder.Encode(pixelData, width, 1);
    var decoded = CcittG3Decoder.Decode(compressed, width, 1);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_MixedScanline_RoundTrips() {
    // First half black, second half white: 11110000
    var width = 8;
    var pixelData = new byte[] { 0b11110000 };

    var compressed = CcittG3Encoder.Encode(pixelData, width, 1);
    var decoded = CcittG3Decoder.Decode(compressed, width, 1);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_MultipleRows_RoundTrips() {
    var width = 16;
    var pixelData = new byte[] {
      0xFF, 0x00, // row 0: 8 black + 8 white
      0x00, 0xFF, // row 1: 8 white + 8 black
    };

    var compressed = CcittG3Encoder.Encode(pixelData, width, 2);
    var decoded = CcittG3Decoder.Decode(compressed, width, 2);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_LongWhiteRun_RoundTrips() {
    // 128 pixels wide, all white — exercises make-up codes
    var width = 128;
    var bytesPerRow = width / 8;
    var pixelData = new byte[bytesPerRow];

    var compressed = CcittG3Encoder.Encode(pixelData, width, 1);
    var decoded = CcittG3Decoder.Decode(compressed, width, 1);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_LongBlackRun_RoundTrips() {
    // 128 pixels wide, all black — exercises make-up codes
    var width = 128;
    var bytesPerRow = width / 8;
    var pixelData = new byte[bytesPerRow];
    Array.Fill(pixelData, (byte)0xFF);

    var compressed = CcittG3Encoder.Encode(pixelData, width, 1);
    var decoded = CcittG3Decoder.Decode(compressed, width, 1);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_SingleBlackPixel_RoundTrips() {
    // Single black pixel at position 0: 10000000
    var width = 8;
    var pixelData = new byte[] { 0b10000000 };

    var compressed = CcittG3Encoder.Encode(pixelData, width, 1);
    var decoded = CcittG3Decoder.Decode(compressed, width, 1);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void Encode_CompressesData() {
    // Large all-white image should compress well
    var width = 1024;
    var bytesPerRow = width / 8;
    var height = 10;
    var pixelData = new byte[bytesPerRow * height]; // all white

    var compressed = CcittG3Encoder.Encode(pixelData, width, height);

    Assert.That(compressed.Length, Is.LessThan(pixelData.Length));
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_NonByteAlignedWidth_RoundTrips() {
    // Width not a multiple of 8
    var width = 13;
    var bytesPerRow = (width + 7) / 8; // 2 bytes
    var pixelData = new byte[] { 0b10110100, 0b11000000 }; // 13 pixels, last 3 bits unused

    var compressed = CcittG3Encoder.Encode(pixelData, width, 1);
    var decoded = CcittG3Decoder.Decode(compressed, width, 1);

    // Only compare the meaningful bits
    for (var x = 0; x < width; ++x) {
      var byteIndex = x >> 3;
      var bitIndex = 7 - (x & 7);
      var expected = (pixelData[byteIndex] >> bitIndex) & 1;
      var actual = (decoded[byteIndex] >> bitIndex) & 1;
      Assert.That(actual, Is.EqualTo(expected), $"Pixel {x} mismatch");
    }
  }
}
