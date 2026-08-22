using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.CreativeYuv.Tests;

/// <summary>
/// The Creative YUV decoder, on packets built here byte by byte.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg on the two real files that exist: the coded shape
/// over 150 frames of <c>samples.ffmpeg.org/V-codecs/CYUV/cyuv.avi</c> (176x144, 4:1:1) and the
/// uncompressed shape over 14 frames of <c>samples.ffmpeg.org/V-codecs/CYUV.AVI</c> (320x240) — every
/// sample of every plane of every frame identical to ffmpeg's own decode of the same bitstream. See
/// <see cref="CreativeYuvVideoDecoder"/>'s own remarks for the two readings that comparison had to
/// settle against the format's own documentation. What follows is the arithmetic underneath that
/// measurement, small enough to state by hand, and it pins each of those two readings against the one
/// it is easy to reach for instead.
/// </remarks>
[TestFixture]
public class CreativeYuvVideoDecoderTests {

  private static readonly CodecTag _Cyuv = CodecTag.FromCharacters("cyuv");

  private static MediaStreamInfo _Stream(
    int width, int height, CodecTag? codec = null, MediaStreamKind kind = MediaStreamKind.Video) => new() {
    Index = 0,
    Kind = kind,
    Codec = codec ?? _Cyuv,
    Width = width,
    Height = height,
    BitsPerPixel = 16,
  };

  /// <summary>Builds a coded packet: the three sixteen-entry tables, then the rows behind them.</summary>
  private static byte[] Coded(byte[] lumaTable, byte[] cbTable, byte[] crTable, params byte[] rows) {
    var packet = new byte[48 + rows.Length];
    lumaTable.CopyTo(packet, 0);
    cbTable.CopyTo(packet, 16);
    crTable.CopyTo(packet, 32);
    rows.CopyTo(packet, 48);

    return packet;
  }

  /// <summary>A table that is zero everywhere except the entries a test names.</summary>
  private static byte[] Table(params (int Index, int Value)[] entries) {
    var table = new byte[16];
    foreach (var (index, value) in entries)
      table[index] = (byte)value;

    return table;
  }

  // ============================================================================================
  // Accepts
  // ============================================================================================

  /// <summary>One of the two real recordings names itself in lower case and the other in upper.</summary>
  [Test]
  [Category("Unit")]
  public void AcceptsTheCyuvTagIgnoringCase() {
    Assert.That(CreativeYuvVideoDecoder.Accepts(_Stream(16, 16)), Is.True);
    Assert.That(CreativeYuvVideoDecoder.Accepts(_Stream(16, 16, CodecTag.FromCharacters("CYUV"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(CreativeYuvVideoDecoder.Accepts(_Stream(16, 16, CodecTag.FromCharacters("CVID"))), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnAudioStream() {
    Assert.That(CreativeYuvVideoDecoder.Accepts(_Stream(16, 16, kind: MediaStreamKind.Audio)), Is.False);
  }

  // ============================================================================================
  // Create
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixels() {
    var failure = Assert.Throws<InvalidDataException>(() => CreativeYuvVideoDecoder.Create(_Stream(0, 16)));
    Assert.That(failure!.Message, Does.Contain("0x16"));
  }

  /// <summary>
  /// Four luminance samples share one chrominance pair and are written three bytes at a time, so a
  /// width that is not a whole number of groups has no reading. Both real recordings are 176 and 320
  /// wide, so the rule costs nothing measured and is refused rather than guessed at.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void RefusesAWidthThatIsNotAWholeNumberOfFourSampleGroups() {
    var failure = Assert.Throws<NotSupportedException>(() => CreativeYuvVideoDecoder.Create(_Stream(6, 16)));
    Assert.That(failure!.Message, Does.Contain("width of 6"));
  }

  // ============================================================================================
  // The coded shape: a 48-byte table block, then three bytes a four-pixel group
  // ============================================================================================

  /// <summary>
  /// One row of eight pixels — two groups — exercising every field the coded shape has: both seed
  /// nibbles, the three luminance indices the opening bytes carry, and a whole following group.
  /// </summary>
  /// <remarks>
  /// The luminance table gives +1 at index 1, +2 at index 2 and -3 at index 3; chrominance moves by
  /// +10 and -6 once each. Worked through: luminance starts at 0x4 &lt;&lt; 4 = 64, then 65, 67, 64 across
  /// the opening bytes and 65, 65, 67, 67 across the group behind them.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ARowIsARunningSumPerComponent() {
    var luma = Table((1, 1), (2, 2), (3, -3 & 0xFF));
    var cb = Table((5, 10));
    var cr = Table((6, -6 & 0xFF));

    // b0: U seed 0x9, Y seed 0x4 | b1: V seed 0x8, Y1 index 1 | b2: Y3 index 3 (high), Y2 index 2 (low)
    // c0: U index 5, Y4 index 1  | c1: V index 6, Y5 index 0  | c2: Y7 index 0 (high), Y6 index 2 (low)
    var packet = Coded(luma, cb, cr, 0x94, 0x81, 0x32, 0x51, 0x60, 0x02);

    var planes = CreativeYuvVideoDecoder.Create(_Stream(8, 1)).DecodeCodedPlanes(packet);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 64, 65, 67, 64, 65, 65, 67, 67 }));
    Assert.That(planes[1], Is.EqualTo(new byte[] { 144, 154 }));
    Assert.That(planes[2], Is.EqualTo(new byte[] { 128, 122 }));
  }

  /// <summary>
  /// The third byte of a group names its fourth luminance sample in the high nibble and its third in
  /// the low one, which is the opposite of what both documents describing this format state.
  /// </summary>
  /// <remarks>
  /// This is the reading a picture would never announce. Read the documented way round, the fourth
  /// sample still comes out right — a running sum does not care which order two differences are added
  /// in — and only the third is wrong, at a plausible value between its two neighbours. The packet
  /// below is built so the two readings disagree: the documented one gives a third sample of 135 where
  /// the measured one gives 168.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void TheThirdGroupByteNamesTheFourthSampleInItsHighNibbleAndTheThirdInItsLow() {
    var luma = Table((1, 7), (2, 40));
    var packet = Coded(luma, Table(), Table(), 0x08, 0x00, 0x12);

    var planes = CreativeYuvVideoDecoder.Create(_Stream(4, 1)).DecodeCodedPlanes(packet);

    // Seed 0x8 << 4 = 128; the second sample's index is 0 and moves nothing; then the low nibble's
    // +40 entry and the high nibble's +7 entry, in that order.
    Assert.That(planes[0], Is.EqualTo(new byte[] { 128, 128, 168, 175 }));
  }

  /// <summary>A seed nibble is the top four bits of its sample, so it widens by shifting rather than
  /// by repeating the pattern.</summary>
  [Test]
  [Category("Unit")]
  public void ASeedNibbleIsTheTopFourBitsOfItsSample() {
    var packet = Coded(Table(), Table(), Table(), 0xF5, 0xA0, 0x00);

    var planes = CreativeYuvVideoDecoder.Create(_Stream(4, 1)).DecodeCodedPlanes(packet);

    // Shifting gives 0xF0, 0x50 and 0xA0; repeating the nibble would give 255, 85 and 170.
    Assert.That(planes[1][0], Is.EqualTo(240), "U seed");
    Assert.That(planes[0][0], Is.EqualTo(80), "Y seed");
    Assert.That(planes[2][0], Is.EqualTo(160), "V seed");
  }

  /// <summary>Chrominance carries one sample a group, and a row's first is its seed rather than a
  /// coded difference.</summary>
  [Test]
  [Category("Unit")]
  public void ChrominanceCarriesOneSampleAGroup() {
    var cb = Table((2, 40));
    var cr = Table((3, 30));
    var packet = Coded(Table(), cb, cr, 0xA5, 0xC0, 0x00, 0x20, 0x30, 0x00);

    var planes = CreativeYuvVideoDecoder.Create(_Stream(8, 1)).DecodeCodedPlanes(packet);

    Assert.That(planes[1], Is.EqualTo(new byte[] { 160, 200 }), "U: seed 0xA0, then +40");
    Assert.That(planes[2], Is.EqualTo(new byte[] { 192, 222 }), "V: seed 0xC0, then +30");
  }

  /// <summary>Every row restarts from its own seed rather than carrying on from the row above.</summary>
  [Test]
  [Category("Unit")]
  public void EachRowRestartsFromItsOwnSeed() {
    var luma = Table((1, 5));
    var packet = Coded(luma, Table(), Table(), 0x02, 0x01, 0x00, 0x09, 0x01, 0x00);

    var planes = CreativeYuvVideoDecoder.Create(_Stream(4, 2)).DecodeCodedPlanes(packet);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 32, 37, 37, 37, 144, 149, 149, 149 }));
  }

  /// <summary>
  /// A sum that leaves the byte range wraps, which is the same arithmetic whether the table entry is
  /// read as a signed number or as a plain byte — the two being congruent modulo 256.
  /// </summary>
  /// <remarks>
  /// Nothing in either real recording reaches this: swept over all 150 coded frames, no running sum of
  /// any component goes below zero or above 255. It is stated here as the choice the decoder makes
  /// rather than as something the files decided.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ARunningSumWraps() {
    var luma = Table((1, 0xF0));
    var packet = Coded(luma, Table(), Table(), 0x00, 0x01, 0x00);

    var planes = CreativeYuvVideoDecoder.Create(_Stream(4, 1)).DecodeCodedPlanes(packet);

    // 0 plus 0xF0 read as -16 is -16, and the byte that holds it is 240.
    Assert.That(planes[0][1], Is.EqualTo(240));
  }

  // ============================================================================================
  // The uncompressed shape: the picture itself, packed U Y V Y, bottom row first
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheUncompressedShapeIsTheSamplesThemselves() {
    var packet = new byte[] { 201, 101, 151, 102, 202, 103, 152, 104 };

    var samples = CreativeYuvVideoDecoder.Create(_Stream(4, 1)).DecodePackedSamples(packet);

    Assert.That(samples, Is.EqualTo(packet));
  }

  [Test]
  [Category("Unit")]
  public void TheUncompressedShapeIsStoredBottomRowFirst() {
    var bottomRow = new byte[] { 11, 10, 12, 10, 11, 10, 12, 10 };
    var topRow = new byte[] { 91, 90, 92, 90, 91, 90, 92, 90 };
    var packet = new byte[16];
    bottomRow.CopyTo(packet, 0);
    topRow.CopyTo(packet, 8);

    var samples = CreativeYuvVideoDecoder.Create(_Stream(4, 2)).DecodePackedSamples(packet);

    Assert.That(samples[..8], Is.EqualTo(topRow), "display row 0 is the coded last row");
    Assert.That(samples[8..], Is.EqualTo(bottomRow), "display row 1 is the coded first row");
  }

  // ============================================================================================
  // Which shape a packet is, read off its own length
  // ============================================================================================

  /// <summary>Nothing in the format says which shape a packet is other than its own length, so a
  /// length that is neither is refused rather than one of them decoded partway.</summary>
  [Test]
  [Category("Unit")]
  public void APacketOfNeitherLengthIsRefused() {
    var decoder = CreativeYuvVideoDecoder.Create(_Stream(8, 1));

    // 8x1 is 48 + 8*1*6/8 = 54 bytes coded and 8*1*2 = 16 uncompressed. Forty is neither.
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[40]), out _));
    Assert.That(failure!.Message, Does.Contain("40 byte(s)"));
    Assert.That(failure.Message, Does.Contain("54"));
    Assert.That(failure.Message, Does.Contain("16"));
  }

  [Test]
  [Category("Unit")]
  public void BothShapesComeBackAsAColourPictureOfTheStatedSize() {
    var decoder = CreativeYuvVideoDecoder.Create(_Stream(4, 2));

    var coded = Coded(Table(), Table(), Table(), 0, 0, 0, 0, 0, 0);
    Assert.That(decoder.TryDecode(new(0, coded), out var fromCoded), Is.True);
    Assert.That(fromCoded.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(fromCoded.Width, Is.EqualTo(4));
    Assert.That(fromCoded.Height, Is.EqualTo(2));
    Assert.That(fromCoded.PixelData!.Length, Is.EqualTo(4 * 2 * 3));

    Assert.That(decoder.TryDecode(new(0, new byte[16]), out var fromPacked), Is.True);
    Assert.That(fromPacked.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(fromPacked.PixelData!.Length, Is.EqualTo(4 * 2 * 3));
  }
}
