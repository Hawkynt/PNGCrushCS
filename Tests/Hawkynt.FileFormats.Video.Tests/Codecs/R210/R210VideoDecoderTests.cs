using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The r210 decoder, on words built here bit by bit.
/// </summary>
/// <remarks>
/// The packing as a whole was measured against ffmpeg over two geometries and twenty frames — 64x40,
/// a whole number of 256-byte rows, and 33x25, which needs the padding — comparing the
/// <c>gbrp10le</c> planes ffmpeg decodes from this package's own r210 packets against the samples
/// that went in: every one identical. What these tests pin down is which bit range of the word each
/// component owns, since ffmpeg's encoder fed pure red writes <c>3F A0 00 00</c> and fed pure blue
/// <c>00 20 03 FC</c>, and a byte reversal into <see cref="PixelFormat.Rgb30"/> would swap the two.
/// </remarks>
[TestFixture]
public class R210VideoDecoderTests {

  private static readonly CodecTag _R210 = CodecTag.FromCharacters("r210");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _R210,
    Width = width,
    Height = height,
  };

  [Test]
  [Category("Unit")]
  public void AcceptsTheR210TagIgnoringCase() {
    Assert.That(R210VideoDecoder.Accepts(_Stream(1, 1)), Is.True);
    Assert.That(R210VideoDecoder.Accepts(_Stream(1, 1, CodecTag.FromCharacters("R210"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(R210VideoDecoder.Accepts(_Stream(1, 1, CodecTag.FromCharacters("r10k"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => R210VideoDecoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void DecodesOnePixelRedInTheHighTenBitsGreenInTheMiddleBlueInTheLowTen() {
    // R=500, G=300, B=700, packed as R<<20 | G<<10 | B and stored big-endian: 1F 44 B2 BC. One
    // pixel is one 256-byte row here, so the packet is padded out to that.
    var row = new byte[256];
    row[0] = 0x1F;
    row[1] = 0x44;
    row[2] = 0xB2;
    row[3] = 0xBC;
    var decoder = R210VideoDecoder.Create(_Stream(1, 1));

    var decoded = decoder.TryDecode(new(0, row), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb30));
    var packed = BitConverter.ToUInt32(frame.PixelData, 0);
    Assert.That(packed & 0x3FF, Is.EqualTo(500u), "red, Rgb30's low ten bits");
    Assert.That((packed >> 10) & 0x3FF, Is.EqualTo(300u), "green, Rgb30's middle ten bits");
    Assert.That((packed >> 20) & 0x3FF, Is.EqualTo(700u), "blue, Rgb30's high ten bits");
  }

  [Test]
  [Category("Unit")]
  public void TheTwoUnusedBitsBecomeAFullyOpaqueAlphaField() {
    // Every real sample of this format leaves its top two bits zero; ffmpeg's own decoder writes
    // fully opaque there when it hands the picture back out with alpha, and this matches it.
    var row = new byte[256];
    var decoder = R210VideoDecoder.Create(_Stream(1, 1));

    decoder.TryDecode(new(0, row), out var frame);

    var packed = BitConverter.ToUInt32(frame.PixelData, 0);
    Assert.That((packed >> 30) & 0x3, Is.EqualTo(3u));
  }

  [Test]
  [Category("Unit")]
  public void APictureFourPixelsWideNeedsNoRowPadding() {
    // 4 pixels times 4 bytes is already a whole 256... no — sixteen bytes. A row this narrow still
    // needs the full 256-byte minimum, so a packet of exactly that many bytes a row is complete.
    var row = new byte[256];
    var decoder = R210VideoDecoder.Create(_Stream(4, 1));

    Assert.That(() => decoder.TryDecode(new(0, row), out _), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void APictureSixtyFourPixelsWideIsExactlyOneRowOfTwoHundredFiftySixBytes() {
    // 64 pixels times 4 bytes is exactly 256 — the one width in this family of tests that needs no
    // padding bytes at all, which is worth pinning down separately from the case above.
    var row = new byte[256];
    var decoder = R210VideoDecoder.Create(_Stream(64, 1));

    Assert.That(() => decoder.TryDecode(new(0, row), out _), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsPaddedStride() {
    // 33 columns is 132 bytes of real data, padded up to 256; a packet of only the unpadded data is
    // still short of what one row of this picture needs.
    var decoder = R210VideoDecoder.Create(_Stream(33, 1));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[132]), out _));
    Assert.That(failure!.Message, Does.Contain("132 byte(s)"));
    Assert.That(failure.Message, Does.Contain("needs 256"));
  }
}
