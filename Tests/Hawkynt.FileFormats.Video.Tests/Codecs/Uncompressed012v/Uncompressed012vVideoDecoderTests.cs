using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Uncompressed012v.Tests;

/// <summary>
/// The 012v decoder, on groups built here word by word: the sixteen-byte packing, the masking its top
/// two bits need, the samples a final group carries past the picture's own width, and the two things
/// the row length is refused for.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg on the one sample that exists —
/// <c>fate-suite.ffmpeg.org/012v/sample.avi</c>, 316x240 in a single 203,520-byte packet — compared
/// against <c>-pix_fmt yuv422p10le</c> at the coded depth rather than through eight-bit colour: all
/// 303,360 bytes of its three planes identical. What follows is the packing underneath that, small
/// enough to state by hand.
/// </remarks>
[TestFixture]
public class Uncompressed012vVideoDecoderTests {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("012v");

  private static MediaStreamInfo _Stream(
    int width, int height, CodecTag? codec = null, MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Codec = codec ?? _Tag,
    Width = width,
    Height = height,
  };

  /// <summary>
  /// Builds one sixteen-byte group from its twelve ten-bit fields, in the order the format packs them:
  /// U0 Y0 V0, Y1 U1 Y2, V1 Y3 U2, Y4 V2 Y5.
  /// </summary>
  private static byte[] Group(
    int u0, int y0, int v0, int y1, int u1, int y2, int v1, int y3, int u2, int y4, int v2, int y5,
    uint spareBits = 0) {
    var group = new byte[16];
    Write(group, 0, u0, y0, v0);
    Write(group, 4, y1, u1, y2);
    Write(group, 8, v1, y3, u2);
    Write(group, 12, y4, v2, y5);

    // The two bits above every ten-bit field, which a real file does not always leave clear.
    if (spareBits != 0)
      BinaryPrimitives.WriteUInt32LittleEndian(
        group, BinaryPrimitives.ReadUInt32LittleEndian(group) | spareBits);

    return group;

    static void Write(byte[] into, int at, int low, int middle, int high) => BinaryPrimitives
      .WriteUInt32LittleEndian(into.AsSpan(at), (uint)(low | (middle << 10) | (high << 20)));
  }

  // ============================================================================================
  // Accepts
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AcceptsThe012vTag() {
    Assert.That(Uncompressed012vVideoDecoder.Accepts(_Stream(6, 1)), Is.True);
  }

  /// <summary>
  /// The reference decoder serves <c>a12v</c> from the same implementation and drops the alpha channel
  /// it carries, saying so in a log line. That is the one code this package will not claim: no sample
  /// of it exists, and the only decoder that reads it announces that it does not read all of it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void RefusesTheAlphaBearingSiblingCode() {
    Assert.That(
      Uncompressed012vVideoDecoder.Accepts(_Stream(6, 1, CodecTag.FromCharacters("a12v"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(Uncompressed012vVideoDecoder.Accepts(_Stream(6, 1, CodecTag.FromCharacters("v210"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream() {
    Assert.That(Uncompressed012vVideoDecoder.Accepts(_Stream(6, 1, kind: MediaStreamKind.Audio)), Is.False);
  }

  // ============================================================================================
  // Create
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => Uncompressed012vVideoDecoder.Create(_Stream(0, 4)));
    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  // ============================================================================================
  // The sixteen-byte group
  // ============================================================================================

  /// <summary>Six luma samples and three chroma pairs, three fields to each of four little-endian
  /// words.</summary>
  [Test]
  [Category("Unit")]
  public void AGroupCarriesSixLumaSamplesAndThreeChromaPairs() {
    var packet = Group(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

    var (luma, cb, cr) = Uncompressed012vVideoDecoder.Create(_Stream(6, 1)).DecodePlanes(packet);

    Assert.That(luma, Is.EqualTo(new ushort[] { 2, 4, 6, 8, 10, 12 }));
    Assert.That(cb, Is.EqualTo(new ushort[] { 1, 5, 9 }));
    Assert.That(cr, Is.EqualTo(new ushort[] { 3, 7, 11 }));
  }

  /// <summary>
  /// The two bits above each ten-bit field are masked off rather than assumed clear — seven of the one
  /// real sample's 50,880 words carry something there, and the reference decoder ignores it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void TheTwoBitsAboveEachFieldAreMaskedOff() {
    var packet = Group(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, spareBits: 0xC0000000);

    var (luma, cb, cr) = Uncompressed012vVideoDecoder.Create(_Stream(6, 1)).DecodePlanes(packet);

    // The spare bits sit in the first word, above its V(0,1) field. Reading them would make it 3075.
    Assert.That(cr[0], Is.EqualTo(3));
    Assert.That(luma[0], Is.EqualTo(2));
    Assert.That(cb[0], Is.EqualTo(1));
  }

  /// <summary>A picture whose width is not a whole number of six-pixel groups still codes a whole
  /// group for its last columns, and the samples past the width are read and thrown away.</summary>
  [Test]
  [Category("Unit")]
  public void SamplesPastTheWidthAreDiscarded() {
    var packet = Group(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);

    var (luma, cb, cr) = Uncompressed012vVideoDecoder.Create(_Stream(4, 1)).DecodePlanes(packet);

    Assert.That(luma, Is.EqualTo(new ushort[] { 2, 4, 6, 8 }));
    Assert.That(cb, Is.EqualTo(new ushort[] { 1, 5 }));
    Assert.That(cr, Is.EqualTo(new ushort[] { 3, 7 }));
  }

  /// <summary>A row is as long as the packet says, which is what separates this format from v210 —
  /// there is no padding rule to compute it from.</summary>
  [Test]
  [Category("Unit")]
  public void RowsAreAsLongAsThePacketDividedByTheHeightSaysAndMayCarryPadding() {
    var first = Group(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
    var second = Group(21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32);

    // Two rows of one group each, with eight bytes of padding behind every group.
    var packet = new byte[2 * 24];
    first.CopyTo(packet, 0);
    second.CopyTo(packet, 24);

    var (luma, _, _) = Uncompressed012vVideoDecoder.Create(_Stream(6, 2)).DecodePlanes(packet);

    Assert.That(luma[..6], Is.EqualTo(new ushort[] { 2, 4, 6, 8, 10, 12 }));
    Assert.That(luma[6..], Is.EqualTo(new ushort[] { 22, 24, 26, 28, 30, 32 }));
  }

  // ============================================================================================
  // What the row length is refused for
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAPacketThatIsNotAWholeNumberOfRows() {
    var decoder = Uncompressed012vVideoDecoder.Create(_Stream(6, 4));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(new byte[66]));
    Assert.That(failure!.Message, Does.Contain("66 byte(s)"));
  }

  /// <summary>
  /// The format permits a final group cut short — a trailing pair of pixels costing five bytes and a
  /// trailing single one two — and no file measured here uses it, the one real sample's rows being 848
  /// bytes where that rule would make them 842. It is refused rather than read under a packing nothing
  /// confirms.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void RefusesARowShorterThanItsWholeGroups() {
    var decoder = Uncompressed012vVideoDecoder.Create(_Stream(6, 1));

    var failure = Assert.Throws<NotSupportedException>(() => decoder.DecodePlanes(new byte[10]));
    Assert.That(failure!.Message, Does.Contain("10 byte(s)"));
    Assert.That(failure.Message, Does.Contain("16"));
  }

  // ============================================================================================
  // What comes out
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFrameComesBackAsAColourPictureOfTheStatedSize() {
    var decoder = Uncompressed012vVideoDecoder.Create(_Stream(6, 1));

    Assert.That(decoder.TryDecode(new(0, Group(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)), out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.Width, Is.EqualTo(6));
    Assert.That(frame.Height, Is.EqualTo(1));
    Assert.That(frame.PixelData!.Length, Is.EqualTo(6 * 3));
  }
}
