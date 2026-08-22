using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The ayuv decoder, on pixels built here byte by byte.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over fifty frames of pseudo-random content at
/// 17x9 — not a whole number of any alignment this format's neighbours use — comparing
/// <see cref="AyuvVideoDecoder.DecodePlanes"/> against ffmpeg's own raw output of the same content
/// before it was packed: every sample of every plane of every frame identical, alpha included. What
/// these tests pin down is the packing itself, since a first measurement built from a mismatched pair
/// of pixel conventions on its two sides appeared to show the name's own letter order and only a
/// second, internally consistent measurement against a real <c>AYUV</c>-tagged packet settled it the
/// other way — see the decoder's own remarks for how that was caught.
/// </remarks>
[TestFixture]
public class AyuvVideoDecoderTests {

  private static readonly CodecTag _Ayuv = CodecTag.FromCharacters("AYUV");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _Ayuv,
    Width = width,
    Height = height,
  };

  [Test]
  [Category("Unit")]
  public void AcceptsTheAyuvTagIgnoringCase() {
    Assert.That(AyuvVideoDecoder.Accepts(_Stream(4, 1)), Is.True);
    Assert.That(AyuvVideoDecoder.Accepts(_Stream(4, 1, CodecTag.FromCharacters("ayuv"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(AyuvVideoDecoder.Accepts(_Stream(4, 1, CodecTag.FromCharacters("v408"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => AyuvVideoDecoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void DecodesTwoPixelsOfVuyaBytes() {
    // V=240, U=90, Y=81, A=255 for the first pixel; V=16, U=128, Y=235, A=0 for the second — the
    // reverse of what the name itself spells, confirmed against a real AYUV-tagged packet.
    var row = new byte[] { 240, 90, 81, 255, 16, 128, 235, 0 };
    var decoder = AyuvVideoDecoder.Create(_Stream(2, 1));

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
    var decoder = AyuvVideoDecoder.Create(_Stream(17, 1));

    Assert.That(() => decoder.DecodePlanes(row), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsStride() {
    var decoder = AyuvVideoDecoder.Create(_Stream(4, 2));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(new byte[20]));
    Assert.That(failure!.Message, Does.Contain("20 byte(s)"));
    Assert.That(failure.Message, Does.Contain("needs 32"));
  }

  [Test]
  [Category("Unit")]
  public void TryDecodeCarriesAlphaThroughUnchanged() {
    var packet = new byte[] { 240, 90, 81, 200, 16, 128, 235, 77 };
    var decoder = AyuvVideoDecoder.Create(_Stream(2, 1));

    var decoded = decoder.TryDecode(new(0, packet), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(frame.PixelData[3], Is.EqualTo(200));
    Assert.That(frame.PixelData[7], Is.EqualTo(77));
  }
}
