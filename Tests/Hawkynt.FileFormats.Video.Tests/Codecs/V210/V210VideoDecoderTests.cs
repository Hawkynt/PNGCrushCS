using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The v210 decoder, on groups built here word by word.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over three geometries and 120 frames — a
/// width that packs into a whole number of six-pixel groups with no row padding at all, and two that
/// need it — comparing <see cref="V210VideoDecoder.DecodePlanes"/> against ffmpeg's own raw
/// <c>yuv422p10le</c> output of the same synthetic content: every sample of every plane of every
/// frame identical. What these tests pin down is the packing itself, word by word, since that
/// comparison alone does not say which bit range of which word a mistake would have hidden in.
/// </remarks>
[TestFixture]
public class V210VideoDecoderTests {

  private static readonly CodecTag _V210 = CodecTag.FromCharacters("v210");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _V210,
    Width = width,
    Height = height,
  };

  // ============================================================================================
  // Accepts / Create
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AcceptsTheV210TagIgnoringCase() {
    Assert.That(V210VideoDecoder.Accepts(_Stream(6, 1)), Is.True);
    Assert.That(V210VideoDecoder.Accepts(_Stream(6, 1, CodecTag.FromCharacters("V210"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(V210VideoDecoder.Accepts(_Stream(6, 1, CodecTag.FromCharacters("r210"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => V210VideoDecoder.Create(_Stream(0, 6)));
    Assert.That(failure!.Message, Does.Contain("0x6"));
  }

  // ============================================================================================
  // The packing: three components in bits 0-9, 10-19 and 20-29 of each little-endian word
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DecodesOneWholeGroupOfSixLumaAndThreeChromaPairs() {
    // Y = 100, 200, 300, 400, 500, 600; U = 10, 30, 50; V = 20, 40, 60, packed exactly as the format
    // states: word 0 is U(0,1), Y(0), V(0,1); word 1 is Y(1), U(2,3), Y(2); word 2 is V(2,3), Y(3),
    // U(4,5); word 3 is Y(4), V(4,5), Y(5).
    var group = new byte[] {
      0x0A, 0x90, 0x41, 0x01, // word 0
      0xC8, 0x78, 0xC0, 0x12, // word 1
      0x28, 0x40, 0x26, 0x03, // word 2
      0xF4, 0xF1, 0x80, 0x25, // word 3
    };
    // Six columns is one group, sixteen bytes — still padded up to the 128-byte row minimum.
    var row = new byte[128];
    group.CopyTo(row, 0);
    var decoder = V210VideoDecoder.Create(_Stream(6, 1));

    var (luma, cb, cr) = decoder.DecodePlanes(row);

    Assert.That(luma, Is.EqualTo(new ushort[] { 100, 200, 300, 400, 500, 600 }));
    Assert.That(cb, Is.EqualTo(new ushort[] { 10, 30, 50 }));
    Assert.That(cr, Is.EqualTo(new ushort[] { 20, 40, 60 }));
  }

  [Test]
  [Category("Unit")]
  public void APictureNarrowerThanOneGroupReadsOnlyItsOwnColumnsAndIgnoresTheRestOfTheGroup() {
    // Same group as above, but the picture is only 2 columns wide: one 16-byte group padded out to
    // the 128-byte row stride. Only Y(0), Y(1) and the U(0,1)/V(0,1) pair the format states belong to
    // them are read; the samples the group carries for columns 2 to 5 are never looked at.
    var group = new byte[] {
      0x0A, 0x90, 0x41, 0x01,
      0xC8, 0x78, 0xC0, 0x12,
      0x28, 0x40, 0x26, 0x03,
      0xF4, 0xF1, 0x80, 0x25,
    };
    var row = new byte[128];
    group.CopyTo(row, 0);
    var decoder = V210VideoDecoder.Create(_Stream(2, 1));

    var (luma, cb, cr) = decoder.DecodePlanes(row);

    Assert.That(luma, Is.EqualTo(new ushort[] { 100, 200 }));
    Assert.That(cb, Is.EqualTo(new ushort[] { 10 }));
    Assert.That(cr, Is.EqualTo(new ushort[] { 20 }));
  }

  [Test]
  [Category("Unit")]
  public void APictureThatIsExactlyEightGroupsWideNeedsNoRowPadding() {
    // 48 columns is eight six-pixel groups, eight times sixteen bytes — a whole 128 already, so a
    // packet of exactly that many bytes a row is complete and nothing past it is expected.
    var row = new byte[128];
    var decoder = V210VideoDecoder.Create(_Stream(48, 1));

    Assert.That(() => decoder.DecodePlanes(row), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsPaddedStride() {
    // 22 columns needs four groups (64 bytes), padded up to the next 128 — so a packet of 64 bytes,
    // exactly the unpadded data, is still short of what one row of this picture needs.
    var decoder = V210VideoDecoder.Create(_Stream(22, 1));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(new byte[64]));
    Assert.That(failure!.Message, Does.Contain("64 byte(s)"));
    Assert.That(failure.Message, Does.Contain("needs 128"));
  }

  // ============================================================================================
  // The packed colour TryDecode hands back
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TryDecodeAlwaysReturnsAPicture() {
    var group = new byte[] {
      0x0A, 0x90, 0x41, 0x01,
      0xC8, 0x78, 0xC0, 0x12,
      0x28, 0x40, 0x26, 0x03,
      0xF4, 0xF1, 0x80, 0x25,
    };
    var row = new byte[128];
    group.CopyTo(row, 0);
    var decoder = V210VideoDecoder.Create(_Stream(6, 1));

    var decoded = decoder.TryDecode(new(0, row), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Width, Is.EqualTo(6));
    Assert.That(frame.Height, Is.EqualTo(1));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Length, Is.EqualTo(6 * 3));
  }
}
