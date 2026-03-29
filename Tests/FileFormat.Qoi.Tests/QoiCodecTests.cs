using System;
using FileFormat.Qoi;

namespace FileFormat.Qoi.Tests;

[TestFixture]
public sealed class QoiCodecTests {

  [Test]
  [Category("Unit")]
  public void Encode_AllSamePixels_UsesRunEncoding() {
    var pixelData = new byte[10 * 3]; // 10 pixels, all black (0,0,0)
    var encoded = QoiCodec.Encode(pixelData, 10, 1, QoiChannels.Rgb);

    // Encoded should be much smaller than raw pixel data + header + end marker
    // All-black pixels: first pixel matches index 0 (all zeros with a=255 hash),
    // remaining 9 pixels form a run
    Assert.That(encoded.Length, Is.LessThan(QoiHeader.StructSize + 10 * 3 + 8));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgb_SinglePixel() {
    var pixelData = new byte[] { 128, 64, 32 };
    var encoded = QoiCodec.Encode(pixelData, 1, 1, QoiChannels.Rgb);

    // Strip header (14 bytes) and end marker (8 bytes) to get raw encoded data
    var rawEncoded = new byte[encoded.Length - QoiHeader.StructSize - 8];
    Array.Copy(encoded, QoiHeader.StructSize, rawEncoded, 0, rawEncoded.Length);

    var decoded = QoiCodec.Decode(rawEncoded, 1, 1, QoiChannels.Rgb);
    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgba_SinglePixel() {
    var pixelData = new byte[] { 100, 150, 200, 128 };
    var encoded = QoiCodec.Encode(pixelData, 1, 1, QoiChannels.Rgba);

    var rawEncoded = new byte[encoded.Length - QoiHeader.StructSize - 8];
    Array.Copy(encoded, QoiHeader.StructSize, rawEncoded, 0, rawEncoded.Length);

    var decoded = QoiCodec.Decode(rawEncoded, 1, 1, QoiChannels.Rgba);
    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgb_MultiplePixels() {
    var pixelData = new byte[] {
      255, 0, 0,   // red
      0, 255, 0,   // green
      0, 0, 255,   // blue
      255, 255, 0  // yellow
    };

    var encoded = QoiCodec.Encode(pixelData, 2, 2, QoiChannels.Rgb);

    var rawEncoded = new byte[encoded.Length - QoiHeader.StructSize - 8];
    Array.Copy(encoded, QoiHeader.StructSize, rawEncoded, 0, rawEncoded.Length);

    var decoded = QoiCodec.Decode(rawEncoded, 2, 2, QoiChannels.Rgb);
    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgba_WithAlphaVariation() {
    var pixelData = new byte[] {
      255, 0, 0, 255,   // opaque red
      255, 0, 0, 128,   // semi-transparent red
      0, 255, 0, 64,    // quarter-transparent green
      0, 0, 255, 0      // fully transparent blue
    };

    var encoded = QoiCodec.Encode(pixelData, 2, 2, QoiChannels.Rgba);

    var rawEncoded = new byte[encoded.Length - QoiHeader.StructSize - 8];
    Array.Copy(encoded, QoiHeader.StructSize, rawEncoded, 0, rawEncoded.Length);

    var decoded = QoiCodec.Decode(rawEncoded, 2, 2, QoiChannels.Rgba);
    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgb_RunOf62Pixels() {
    // Exactly 62 identical pixels = max single run
    var pixelData = new byte[62 * 3];
    for (var i = 0; i < 62; ++i) {
      pixelData[i * 3] = 42;
      pixelData[i * 3 + 1] = 84;
      pixelData[i * 3 + 2] = 126;
    }

    var encoded = QoiCodec.Encode(pixelData, 62, 1, QoiChannels.Rgb);

    var rawEncoded = new byte[encoded.Length - QoiHeader.StructSize - 8];
    Array.Copy(encoded, QoiHeader.StructSize, rawEncoded, 0, rawEncoded.Length);

    var decoded = QoiCodec.Decode(rawEncoded, 62, 1, QoiChannels.Rgb);
    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgb_RunExceeds62Pixels() {
    // 70 identical pixels = should produce multiple run ops
    var pixelData = new byte[70 * 3];
    for (var i = 0; i < 70; ++i) {
      pixelData[i * 3] = 42;
      pixelData[i * 3 + 1] = 84;
      pixelData[i * 3 + 2] = 126;
    }

    var encoded = QoiCodec.Encode(pixelData, 70, 1, QoiChannels.Rgb);

    var rawEncoded = new byte[encoded.Length - QoiHeader.StructSize - 8];
    Array.Copy(encoded, QoiHeader.StructSize, rawEncoded, 0, rawEncoded.Length);

    var decoded = QoiCodec.Decode(rawEncoded, 70, 1, QoiChannels.Rgb);
    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgb_SmallDifferences() {
    // Pixels with small diffs (should use QOI_OP_DIFF)
    var pixelData = new byte[] {
      100, 100, 100,
      101, 99, 100,  // dr=+1, dg=-1, db=0 (within [-2,1])
      100, 100, 101  // dr=-1, dg=+1, db=+1
    };

    var encoded = QoiCodec.Encode(pixelData, 3, 1, QoiChannels.Rgb);

    var rawEncoded = new byte[encoded.Length - QoiHeader.StructSize - 8];
    Array.Copy(encoded, QoiHeader.StructSize, rawEncoded, 0, rawEncoded.Length);

    var decoded = QoiCodec.Decode(rawEncoded, 3, 1, QoiChannels.Rgb);
    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgb_LumaDifferences() {
    // Pixels with medium diffs (should use QOI_OP_LUMA)
    var pixelData = new byte[] {
      100, 100, 100,
      110, 115, 108  // dg=+15 (in [-32,31]), dr-dg=-5 (in [-8,7]), db-dg=-7 (in [-8,7])
    };

    var encoded = QoiCodec.Encode(pixelData, 2, 1, QoiChannels.Rgb);

    var rawEncoded = new byte[encoded.Length - QoiHeader.StructSize - 8];
    Array.Copy(encoded, QoiHeader.StructSize, rawEncoded, 0, rawEncoded.Length);

    var decoded = QoiCodec.Decode(rawEncoded, 2, 1, QoiChannels.Rgb);
    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgb_IndexLookup() {
    // Pattern: A, B, A (second A should use index lookup)
    var pixelData = new byte[] {
      200, 100, 50,
      50, 100, 200,
      200, 100, 50
    };

    var encoded = QoiCodec.Encode(pixelData, 3, 1, QoiChannels.Rgb);

    var rawEncoded = new byte[encoded.Length - QoiHeader.StructSize - 8];
    Array.Copy(encoded, QoiHeader.StructSize, rawEncoded, 0, rawEncoded.Length);

    var decoded = QoiCodec.Decode(rawEncoded, 3, 1, QoiChannels.Rgb);
    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void Encode_Rgb_CompressesUniformImage() {
    // All-white image: should compress very well
    var pixelData = new byte[100 * 100 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 255;

    var encoded = QoiCodec.Encode(pixelData, 100, 100, QoiChannels.Rgb);

    // Raw would be 30000 bytes + header + end marker
    // With run encoding, should be much smaller
    Assert.That(encoded.Length, Is.LessThan(pixelData.Length / 2));
  }
}
