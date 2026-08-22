using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The v308 decoder, on pixels built here byte by byte.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over fifty frames of pseudo-random content at
/// 17x9 — not a whole number of any alignment this format's neighbours use — comparing
/// <see cref="V308VideoDecoder.DecodePlanes"/> against ffmpeg's own raw output of the same content
/// before it was packed: every sample of every plane of every frame identical. What these tests pin
/// down is the packing itself, since that comparison alone does not say which byte of a pixel is
/// which sample.
/// </remarks>
[TestFixture]
public class V308VideoDecoderTests {

  private static readonly CodecTag _V308 = CodecTag.FromCharacters("v308");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _V308,
    Width = width,
    Height = height,
  };

  [Test]
  [Category("Unit")]
  public void AcceptsTheV308TagIgnoringCase() {
    Assert.That(V308VideoDecoder.Accepts(_Stream(4, 1)), Is.True);
    Assert.That(V308VideoDecoder.Accepts(_Stream(4, 1, CodecTag.FromCharacters("V308"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(V308VideoDecoder.Accepts(_Stream(4, 1, CodecTag.FromCharacters("v408"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => V308VideoDecoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void DecodesTwoPixelsOfVyuBytes() {
    // V=240, Y=81, U=90 for the first pixel; V=16, Y=235, U=128 for the second.
    var row = new byte[] { 240, 81, 90, 16, 235, 128 };
    var decoder = V308VideoDecoder.Create(_Stream(2, 1));

    var (luma, cb, cr) = decoder.DecodePlanes(row);

    Assert.That(luma, Is.EqualTo(new byte[] { 81, 235 }));
    Assert.That(cb, Is.EqualTo(new byte[] { 90, 128 }));
    Assert.That(cr, Is.EqualTo(new byte[] { 240, 16 }));
  }

  [Test]
  [Category("Unit")]
  public void ARowIsExactlyWidthTimesThreeBytesWithNoPaddingAtAll() {
    var row = new byte[17 * 3];
    var decoder = V308VideoDecoder.Create(_Stream(17, 1));

    Assert.That(() => decoder.DecodePlanes(row), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsStride() {
    var decoder = V308VideoDecoder.Create(_Stream(4, 2));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(new byte[20]));
    Assert.That(failure!.Message, Does.Contain("20 byte(s)"));
    Assert.That(failure.Message, Does.Contain("needs 24"));
  }

  [Test]
  [Category("Unit")]
  public void TryDecodeAlwaysReturnsAPicture() {
    var packet = new byte[4 * 1 * 3];
    var decoder = V308VideoDecoder.Create(_Stream(4, 1));

    var decoded = decoder.TryDecode(new(0, packet), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Width, Is.EqualTo(4));
    Assert.That(frame.Height, Is.EqualTo(1));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Length, Is.EqualTo(4 * 3));
  }
}
