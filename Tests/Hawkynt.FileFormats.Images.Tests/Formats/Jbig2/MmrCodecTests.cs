using System;
using FileFormat.Jbig2;

namespace FileFormat.Jbig2.Tests;

[TestFixture]
public sealed class MmrCodecTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllWhite() {
    var width = 32;
    var height = 4;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height]; // all zeros = all white

    var encoded = MmrCodec.Encode(pixelData, width, height);
    var decoded = MmrCodec.Decode(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllBlack() {
    var width = 32;
    var height = 4;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    Array.Fill(pixelData, (byte)0xFF);

    var encoded = MmrCodec.Encode(pixelData, width, height);
    var decoded = MmrCodec.Decode(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Alternating() {
    var width = 16;
    var height = 2;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    pixelData[0] = 0b10101010;
    pixelData[1] = 0b10101010;
    pixelData[2] = 0b01010101;
    pixelData[3] = 0b01010101;

    var encoded = MmrCodec.Encode(pixelData, width, height);
    var decoded = MmrCodec.Decode(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SingleRow() {
    var width = 8;
    var height = 1;
    var pixelData = new byte[] { 0b11001100 };

    var encoded = MmrCodec.Encode(pixelData, width, height);
    var decoded = MmrCodec.Decode(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedPattern() {
    var width = 16;
    var height = 4;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    pixelData[0] = 0xFF;
    pixelData[2] = 0x00; pixelData[3] = 0xFF;
    pixelData[4] = 0b10101010; pixelData[5] = 0b01010101;
    pixelData[6] = 0b11001100; pixelData[7] = 0b00110011;

    var encoded = MmrCodec.Encode(pixelData, width, height);
    var decoded = MmrCodec.Decode(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 8;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    for (var row = 0; row < height; ++row)
      for (var byteIdx = 0; byteIdx < bytesPerRow; ++byteIdx)
        pixelData[row * bytesPerRow + byteIdx] = (byte)(byteIdx < row ? 0xFF : 0x00);

    var encoded = MmrCodec.Encode(pixelData, width, height);
    var decoded = MmrCodec.Decode(encoded, width, height);

    Assert.That(decoded, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void Encode_AllWhite_CompressedSmallerThanRaw() {
    var width = 256;
    var height = 16;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    var encoded = MmrCodec.Encode(pixelData, width, height);

    Assert.That(encoded.Length, Is.LessThan(pixelData.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_NonByteBoundaryWidth() {
    var width = 13;
    var height = 3;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    pixelData[0] = 0b11111111;
    pixelData[1] = 0b11111000; // only 5 MSBits used

    var encoded = MmrCodec.Encode(pixelData, width, height);
    var decoded = MmrCodec.Decode(encoded, width, height);

    // Compare only the valid bits
    for (var row = 0; row < height; ++row)
      for (var x = 0; x < width; ++x) {
        var byteIdx = row * bytesPerRow + (x >> 3);
        var bitIdx = 7 - (x & 7);
        var origBit = (pixelData[byteIdx] >> bitIdx) & 1;
        var decodedBit = (decoded[byteIdx] >> bitIdx) & 1;
        Assert.That(decodedBit, Is.EqualTo(origBit), $"Mismatch at row {row}, pixel {x}");
      }
  }
}
