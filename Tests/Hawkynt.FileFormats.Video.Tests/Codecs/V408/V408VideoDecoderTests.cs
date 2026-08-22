using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The v408 decoder, on pixels built here byte by byte.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over fifty frames of pseudo-random content at
/// 17x9 — not a whole number of any alignment this format's neighbours use — comparing
/// <see cref="V408VideoDecoder.DecodePlanes"/> against ffmpeg's own raw output of the same content
/// before it was packed: every sample of every plane of every frame identical, alpha included. What
/// these tests pin down is the packing itself, since that comparison alone does not say which byte of
/// a pixel is which sample.
/// </remarks>
[TestFixture]
public class V408VideoDecoderTests {

  private static readonly CodecTag _V408 = CodecTag.FromCharacters("v408");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _V408,
    Width = width,
    Height = height,
  };

  [Test]
  [Category("Unit")]
  public void AcceptsTheV408TagIgnoringCase() {
    Assert.That(V408VideoDecoder.Accepts(_Stream(4, 1)), Is.True);
    Assert.That(V408VideoDecoder.Accepts(_Stream(4, 1, CodecTag.FromCharacters("V408"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(V408VideoDecoder.Accepts(_Stream(4, 1, CodecTag.FromCharacters("v308"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => V408VideoDecoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void DecodesTwoPixelsOfUyvaBytes() {
    // U=90, Y=81, V=240, A=255 for the first pixel; U=128, Y=235, V=16, A=0 for the second.
    var row = new byte[] { 90, 81, 240, 255, 128, 235, 16, 0 };
    var decoder = V408VideoDecoder.Create(_Stream(2, 1));

    var (luma, cb, cr, alpha) = decoder.DecodePlanes(row);

    Assert.That(luma, Is.EqualTo(new byte[] { 81, 235 }));
    Assert.That(cb, Is.EqualTo(new byte[] { 90, 128 }));
    Assert.That(cr, Is.EqualTo(new byte[] { 240, 16 }));
    Assert.That(alpha, Is.EqualTo(new byte[] { 255, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void ARowIsExactlyWidthTimesFourBytesWithNoPaddingAtAll() {
    var row = new byte[17 * 4];
    var decoder = V408VideoDecoder.Create(_Stream(17, 1));

    Assert.That(() => decoder.DecodePlanes(row), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsStride() {
    var decoder = V408VideoDecoder.Create(_Stream(4, 2));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(new byte[20]));
    Assert.That(failure!.Message, Does.Contain("20 byte(s)"));
    Assert.That(failure.Message, Does.Contain("needs 32"));
  }

  [Test]
  [Category("Unit")]
  public void TryDecodeCarriesAlphaThroughUnchanged() {
    var packet = new byte[] { 90, 81, 240, 200, 128, 235, 16, 77 };
    var decoder = V408VideoDecoder.Create(_Stream(2, 1));

    var decoded = decoder.TryDecode(new(0, packet), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(frame.PixelData[3], Is.EqualTo(200));
    Assert.That(frame.PixelData[7], Is.EqualTo(77));
  }
}
