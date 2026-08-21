using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The r10k decoder, on words built here bit by bit.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg's own encoder over three geometries and 90
/// frames — 8x2, 33x25 and 64x40 — comparing every word this decoder produces against the
/// <c>gbrp10le</c> planes that went into the encoder: every one identical, with no row padding found
/// at any of the three widths. What these tests pin down is the packing itself, since that comparison
/// alone does not say which bit range of the word a mistake would have hidden in — and this format's
/// bit ranges are not r210's, despite the two looking related by name.
/// </remarks>
[TestFixture]
public class R10kVideoDecoderTests {

  private static readonly CodecTag _R10k = CodecTag.FromCharacters("R10k");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _R10k,
    Width = width,
    Height = height,
  };

  [Test]
  [Category("Unit")]
  public void AcceptsTheR10kTagIgnoringCase() {
    Assert.That(R10kVideoDecoder.Accepts(_Stream(1, 1)), Is.True);
    Assert.That(R10kVideoDecoder.Accepts(_Stream(1, 1, CodecTag.FromCharacters("r10K"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(R10kVideoDecoder.Accepts(_Stream(1, 1, CodecTag.FromCharacters("r210"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => R10kVideoDecoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void DecodesOnePixelRedHighGreenMiddleBlueNextWithTheTwoUnusedBitsAtTheBottom() {
    // R=500, G=300, B=700, packed as R<<22 | G<<12 | B<<2 and stored big-endian: 7D 12 CA F0. One
    // pixel is one row here, and this format needs no padding at all.
    var row = new byte[] { 0x7D, 0x12, 0xCA, 0xF0 };
    var decoder = R10kVideoDecoder.Create(_Stream(1, 1));

    var decoded = decoder.TryDecode(new(0, row), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb30));
    var packed = BitConverter.ToUInt32(frame.PixelData, 0);
    Assert.That(packed & 0x3FF, Is.EqualTo(500u), "red, Rgb30's low ten bits");
    Assert.That((packed >> 10) & 0x3FF, Is.EqualTo(300u), "green, Rgb30's middle ten bits");
    Assert.That((packed >> 20) & 0x3FF, Is.EqualTo(700u), "blue, Rgb30's high ten bits");
    Assert.That((packed >> 30) & 0x3, Is.EqualTo(3u), "fully opaque, matching ffmpeg's own decode");
  }

  [Test]
  [Category("Unit")]
  public void ARowIsExactlyWidthTimesFourBytesWithNoPaddingAtAll() {
    // Unlike r210's family, nothing here pads a row out to any alignment — a packet of exactly
    // width times four bytes a row is complete.
    var row = new byte[33 * 4];
    var decoder = R10kVideoDecoder.Create(_Stream(33, 1));

    Assert.That(() => decoder.TryDecode(new(0, row), out _), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsStride() {
    var decoder = R10kVideoDecoder.Create(_Stream(4, 2));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[20]), out _));
    Assert.That(failure!.Message, Does.Contain("20 byte(s)"));
    Assert.That(failure.Message, Does.Contain("needs 32"));
  }
}
