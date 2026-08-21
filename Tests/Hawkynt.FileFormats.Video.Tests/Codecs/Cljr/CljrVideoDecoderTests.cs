using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The CLJR decoder, on words built here bit by bit.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg's own decode of what its encoder wrote — the
/// oracle this format needs, because it dithers, so a coded word does not equal a plain quantisation
/// of the source and only another decoder's reading of the same bits is a fact the encoder can be
/// checked against. Three geometries and sixty frames of pseudo-random <c>yuv411p</c> content: every
/// sample of every plane of every frame identical. What these tests pin down is the packing itself and
/// the two different eight-bit expansions, since that comparison alone does not say which bit range of
/// the word, or which widening rule, a mistake would have hidden behind.
/// </remarks>
[TestFixture]
public class CljrVideoDecoderTests {

  private static readonly CodecTag _Cljr = CodecTag.FromCharacters("CLJR");

  private static MediaStreamInfo _Stream(int width, int height, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _Cljr,
    Width = width,
    Height = height,
  };

  [Test]
  [Category("Unit")]
  public void AcceptsTheCljrTagIgnoringCase() {
    Assert.That(CljrVideoDecoder.Accepts(_Stream(4, 1)), Is.True);
    Assert.That(CljrVideoDecoder.Accepts(_Stream(4, 1, CodecTag.FromCharacters("cljr"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(CljrVideoDecoder.Accepts(_Stream(4, 1, CodecTag.FromCharacters("Y41P"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => CljrVideoDecoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAWidthThatIsNotAWholeNumberOfFourPixelGroups() {
    var failure = Assert.Throws<NotSupportedException>(() => CljrVideoDecoder.Create(_Stream(10, 4)));
    Assert.That(failure!.Message, Does.Contain("10"));
  }

  // ============================================================================================
  // The word: the four luma samples in reverse order, then the shared chroma pair
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DecodesOneGroupOfFourLumaCodesInReverseOrderThenTheSharedChromaPair() {
    // Codes Y0=1, Y1=2, Y2=3, Y3=4, U=10, V=20, packed as Y3<<27 | Y2<<22 | Y1<<17 | Y0<<12 | U<<6 |
    // V and stored big-endian: 20 C4 12 94.
    var group = new byte[] { 0x20, 0xC4, 0x12, 0x94 };
    var decoder = CljrVideoDecoder.Create(_Stream(4, 1));

    var (luma, cb, cr) = decoder.DecodePlanes(group);

    Assert.That(luma, Is.EqualTo(new byte[] { 8, 16, 24, 33 }));
    Assert.That(cb, Is.EqualTo(new byte[] { 40 }));
    Assert.That(cr, Is.EqualTo(new byte[] { 80 }));
  }

  [Test]
  [Category("Unit")]
  public void LumaWidensByReplicatingItsOwnTopThreeBitsIntoTheLowThree() {
    // Code 31, the highest five-bit value, must reach 255 and not fall short of it — replication
    // does; a plain shift alone would leave the low three bits at zero and land on 248.
    var group = new byte[] { 0xF8, 0x00, 0x00, 0x00 }; // Y3 = 31, everything else zero
    var decoder = CljrVideoDecoder.Create(_Stream(4, 1));

    var (luma, _, _) = decoder.DecodePlanes(group);

    Assert.That(luma[3], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void ChromaWidensByAPlainShiftWithTheLowTwoBitsLeftAtZero() {
    // Code 41 for blue must decode to 164 exactly — 41 << 2 — and not the 166 replicating the top
    // two bits into the low two would give, which is where luma's rule and chroma's rule differ.
    var group = new byte[] { 0x00, 0x00, 0x0A, 0x40 }; // U = 41 (bits 6-11), V = 0
    var decoder = CljrVideoDecoder.Create(_Stream(4, 1));

    var (_, cb, _) = decoder.DecodePlanes(group);

    Assert.That(cb[0], Is.EqualTo(164));
  }

  [Test]
  [Category("Unit")]
  public void RowsRunTopToBottom() {
    // Two rows of one group each, all luma codes zero but for one marker bit that tells the rows
    // apart: row 0 codes a nonzero Y0, row 1 does not.
    var row0 = new byte[] { 0x00, 0x00, 0x10, 0x00 }; // Y0 = 1 -> widened to 8
    var row1 = new byte[] { 0x00, 0x00, 0x00, 0x00 };
    var packet = new byte[8];
    row0.CopyTo(packet, 0);
    row1.CopyTo(packet, 4);
    var decoder = CljrVideoDecoder.Create(_Stream(4, 2));

    var (luma, _, _) = decoder.DecodePlanes(packet);

    Assert.That(luma[0], Is.EqualTo(8), "display row 0 is the coded first row");
    Assert.That(luma[4], Is.EqualTo(0), "display row 1 is the coded second row");
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPacketShorterThanItsStride() {
    var decoder = CljrVideoDecoder.Create(_Stream(4, 2));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(new byte[4]));
    Assert.That(failure!.Message, Does.Contain("4 byte(s)"));
    Assert.That(failure.Message, Does.Contain("needs 8"));
  }

  [Test]
  [Category("Unit")]
  public void TryDecodeAlwaysReturnsAPicture() {
    var group = new byte[] { 0x20, 0xC4, 0x12, 0x94 };
    var decoder = CljrVideoDecoder.Create(_Stream(4, 1));

    var decoded = decoder.TryDecode(new(0, group), out var frame);

    Assert.That(decoded, Is.True);
    Assert.That(frame.Width, Is.EqualTo(4));
    Assert.That(frame.Height, Is.EqualTo(1));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Length, Is.EqualTo(4 * 3));
  }
}
