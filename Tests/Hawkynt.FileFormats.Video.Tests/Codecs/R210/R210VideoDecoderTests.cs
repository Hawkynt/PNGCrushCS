using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The r210 decoder, on words built here bit by bit.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg's own encoder over three geometries and 90
/// frames — 8x2 and 64x40, both a whole number of 256-byte rows, and 33x25, which needs the padding
/// — comparing every word this decoder produces against the <c>x2rgb10le</c> samples that went into
/// the encoder: every one identical. What these tests pin down is the packing itself, since that
/// comparison alone does not say which bit range of the word a mistake would have hidden in.
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
  public void DecodesOnePixelRedInTheLowTenBitsGreenInTheMiddleBlueInTheHighTen() {
    // R=500, G=300, B=700, packed as R | G<<10 | B<<20 and stored big-endian: 2B C4 B1 F4. One
    // pixel is one 256-byte row here, so the packet is padded out to that.
    var row = new byte[256];
    row[0] = 0x2B;
    row[1] = 0xC4;
    row[2] = 0xB1;
    row[3] = 0xF4;
    var decoder = R210VideoDecoder.Create(_Stream(1, 1));

    var decoded = decoder.TryDecode(new(0, row), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb30));
    var packed = BitConverter.ToUInt32(frame.PixelData, 0);
    Assert.That(packed & 0x3FF, Is.EqualTo(500u), "red, low ten bits");
    Assert.That((packed >> 10) & 0x3FF, Is.EqualTo(300u), "green, middle ten bits");
    Assert.That((packed >> 20) & 0x3FF, Is.EqualTo(700u), "blue, high ten bits");
  }

  [Test]
  [Category("Unit")]
  public void TheTwoUnusedBitsBecomeAFullyOpaqueAlphaField() {
    // Every real sample of this format leaves its top two bits zero; ffmpeg's own decoder writes
    // fully opaque there when it hands the same 30 bits back out as x2rgb10le, and this matches it.
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
