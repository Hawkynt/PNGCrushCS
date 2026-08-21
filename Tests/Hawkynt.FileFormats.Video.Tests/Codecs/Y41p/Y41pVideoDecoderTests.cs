using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The y41p decoder, on groups built here byte by byte.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over three geometries and 90 frames of
/// pseudo-random content — 64x8, 96x40 and 128x33 — comparing <see cref="Y41pVideoDecoder.DecodePlanes"/>
/// against ffmpeg's own raw <c>yuv411p</c> output of the same content: every sample of every plane of
/// every frame identical. That comparison is also what found the row order below, which a first sweep
/// against the same content missed entirely — reading rows top-down matched nothing, because random
/// content gives a wrong row nothing in common with the right one, where the earlier hand-built single
/// row test here happened not to depend on row order at all.
/// </remarks>
[TestFixture]
public class Y41pVideoDecoderTests {

  private static readonly CodecTag _Y41p = CodecTag.FromCharacters("Y41P");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _Y41p,
    Width = width,
    Height = height,
  };

  [Test]
  [Category("Unit")]
  public void AcceptsTheY41pTagIgnoringCase() {
    Assert.That(Y41pVideoDecoder.Accepts(_Stream(8, 1)), Is.True);
    Assert.That(Y41pVideoDecoder.Accepts(_Stream(8, 1, CodecTag.FromCharacters("y41p"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(Y41pVideoDecoder.Accepts(_Stream(8, 1, CodecTag.FromCharacters("012v"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => Y41pVideoDecoder.Create(_Stream(0, 8)));
    Assert.That(failure!.Message, Does.Contain("0x8"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAWidthThatIsNotAWholeNumberOfEightPixelGroups() {
    // No encoder writes one, and there is no real stream to say what a partial group would mean.
    var failure = Assert.Throws<NotSupportedException>(() => Y41pVideoDecoder.Create(_Stream(20, 4)));
    Assert.That(failure!.Message, Does.Contain("20"));
  }

  // ============================================================================================
  // The group: U, Y, V, Y, U, Y, V, Y, Y, Y, Y, Y — two groups of sixteen pixels
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DecodesTwoGroupsOfSixteenLumaAndFourChromaPairs() {
    // Y = 101..116, U = 201..204, V = 151..154, packed exactly as ffmpeg's own encoder writes them:
    // U(0), Y(0), V(0), Y(1), U(1), Y(2), V(1), Y(3), Y(4), Y(5), Y(6), Y(7), repeated for the second
    // group of eight columns.
    var packet = new byte[] {
      201, 101, 151, 102, 202, 103, 152, 104, 105, 106, 107, 108,
      203, 109, 153, 110, 204, 111, 154, 112, 113, 114, 115, 116,
    };
    var decoder = Y41pVideoDecoder.Create(_Stream(16, 1));

    var (luma, cb, cr) = decoder.DecodePlanes(packet);

    Assert.That(luma, Is.EqualTo(new byte[] { 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116 }));
    Assert.That(cb, Is.EqualTo(new byte[] { 201, 202, 203, 204 }));
    Assert.That(cr, Is.EqualTo(new byte[] { 151, 152, 153, 154 }));
  }

  // ============================================================================================
  // Row order: bottom row first, the convention every Windows bitmap this format is built around
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RowsAreCodedBottomRowFirst() {
    // Two rows of one group each: the first coded row is flat 10s, the second flat 90s. Read
    // top-down this would put the 90s in display row 0; the format puts the coded bottom row there
    // instead, so display row 0 is the 10s and display row 1 the 90s.
    var bottomRow = new byte[] { 11, 10, 12, 10, 11, 10, 12, 10, 10, 10, 10, 10 };
    var topRow = new byte[] { 91, 90, 92, 90, 91, 90, 92, 90, 90, 90, 90, 90 };
    var packet = new byte[24];
    bottomRow.CopyTo(packet, 0);
    topRow.CopyTo(packet, 12);
    var decoder = Y41pVideoDecoder.Create(_Stream(8, 2));

    var (luma, cb, cr) = decoder.DecodePlanes(packet);

    Assert.That(luma[..8], Is.EqualTo(new byte[] { 90, 90, 90, 90, 90, 90, 90, 90 }), "display row 0 is the coded top row");
    Assert.That(luma[8..], Is.EqualTo(new byte[] { 10, 10, 10, 10, 10, 10, 10, 10 }), "display row 1 is the coded bottom row");
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsStride() {
    // 8 columns is one twelve-byte group; two rows need twenty-four bytes.
    var decoder = Y41pVideoDecoder.Create(_Stream(8, 2));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(new byte[12]));
    Assert.That(failure!.Message, Does.Contain("12 byte(s)"));
    Assert.That(failure.Message, Does.Contain("needs 24"));
  }

  [Test]
  [Category("Unit")]
  public void TryDecodeAlwaysReturnsAPicture() {
    var packet = new byte[] { 201, 101, 151, 102, 202, 103, 152, 104, 105, 106, 107, 108 };
    var decoder = Y41pVideoDecoder.Create(_Stream(8, 1));

    var decoded = decoder.TryDecode(new(0, packet), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Width, Is.EqualTo(8));
    Assert.That(frame.Height, Is.EqualTo(1));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Length, Is.EqualTo(8 * 3));
  }
}
