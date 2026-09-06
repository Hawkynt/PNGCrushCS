using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Indeo 2's codes, its two frame types and its delta tables — on frames built here bit by bit, and on
/// six frames of a real file measured against ffmpeg's own decode of them.
/// </summary>
/// <remarks>
/// The synthetic frames are the smallest picture the codec can hold, 8x4, so that what every code did
/// can be written out sample by sample. They pin down what the real-file comparison only shows the
/// result of: which of the two bytes of a delta-table entry lands on which sample, that a run means
/// twice its stated length, that an intra frame's first line is absolute and its other lines are
/// differences from the line above, and that an inter frame's deltas are three quarters as strong.
/// <para/>
/// The codes are written into the bit stream least-significant-bit first, and the code values are the
/// canonical ones for the lengths the table states: four codes of three bits, then codes of five bits
/// from <c>10000</c> upwards. Nothing here re-derives the table — it names four codes it knows and
/// uses only those.
/// </remarks>
[TestFixture]
public sealed class Indeo2VideoDecoderTests {

  // The first four codes of the table, which are all three bits: symbols 0x01, 0x02, 0x80 and 0x03.
  private const int _CODE_DELTA_1 = 0b000;
  private const int _CODE_RUN_OF_TWO = 0b010;
  private const int _CODE_LENGTH = 3;

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheRt21CodeIsTaken() => Assert.That(Indeo2VideoDecoder.Accepts(_Stream("RT21", 8, 4)), Is.True);

  [Test]
  [Category("Unit")]
  public void TheRt21CodeIsTakenInLowerCase() => Assert.That(Indeo2VideoDecoder.Accepts(_Stream("rt21", 8, 4)), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() => Assert.That(Indeo2VideoDecoder.Accepts(_Stream("IV32", 8, 4)), Is.False);

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("RT21"),
    };

    Assert.That(Indeo2VideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream("RT21", 8, 4);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Intel Indeo 2"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Indeo2VideoDecoder>());
  }

  // ============================================================================================
  // Creation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureWithNoPixelsRefuses()
    => Assert.Throws<InvalidDataException>(() => Indeo2VideoDecoder.Create(_Stream("RT21", 0, 0)));

  [Test]
  [Category("Unit")]
  public void AWidthThatDoesNotDivideByEightRefuses() {
    // Four divides the chrominance planes out of it, but each of them would then be an odd number of
    // samples wide and every code writes a pair.
    var failure = Assert.Throws<NotSupportedException>(() => Indeo2VideoDecoder.Create(_Stream("RT21", 4, 4)));
    Assert.That(failure!.Message, Does.Contain("4x4"));
  }

  [Test]
  [Category("Unit")]
  public void AHeightThatDoesNotDivideByFourRefuses()
    => Assert.Throws<NotSupportedException>(() => Indeo2VideoDecoder.Create(_Stream("RT21", 8, 6)));

  // ============================================================================================
  // The frame header
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFrameShorterThanItsOwnHeaderRefuses() {
    var decoder = Indeo2VideoDecoder.Create(_Stream("RT21", 8, 4));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[48]), out _));
    Assert.That(failure!.Message, Does.Contain("48-byte header"));
  }

  [Test]
  [Category("Unit")]
  public void AChrominanceTableIndexOutsideTheFourDefinedRefuses() {
    var decoder = Indeo2VideoDecoder.Create(_Stream("RT21", 8, 4));
    var frame = new byte[64];
    frame[18] = 1;
    frame[0x22] = 0x10; // chrominance index 4, of the four numbered 0 to 3.

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, frame), out _));
    Assert.That(failure!.Message, Does.Contain("delta table 4"));
  }

  [Test]
  [Category("Unit")]
  public void ABitPatternThatIsNoCodeRefuses() {
    var decoder = Indeo2VideoDecoder.Create(_Stream("RT21", 8, 4));

    // Every bit set spells no code: the longest code is fourteen bits and none of them is all ones.
    var frame = new byte[64];
    frame[18] = 1;
    for (var i = 48; i < frame.Length; ++i)
      frame[i] = 0xFF;

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, frame), out _));
  }

  // ============================================================================================
  // Intra frames
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnIntraFramesFirstLineTakesTableEntriesAsSampleValues() {
    var decoder = Indeo2VideoDecoder.Create(_Stream("RT21", 8, 4));
    var bits = new Indeo2TestBitWriter();

    // First line: one delta code for the first pair, then three runs of two for the rest.
    bits.WriteCode(_CODE_DELTA_1, _CODE_LENGTH);
    _WriteRuns(bits, 3);
    _WriteSkippedLines(bits, 3);
    _WriteSkippedChroma(bits);

    decoder.TryDecode(new(0, _Frame(bits, intra: true)), out _);

    // Code 1 addresses entries 2 and 3 of delta table 0, both 0x84; a run writes the neutral 0x80.
    var expected = new byte[] { 0x84, 0x84, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 };
    Assert.That(decoder.Planes.Luma.Take(8), Is.EqualTo(expected).AsCollection);
  }

  [Test]
  [Category("Unit")]
  public void AnIntraFramesRunRepeatsTheLineAboveIt() {
    var decoder = Indeo2VideoDecoder.Create(_Stream("RT21", 8, 4));
    var bits = new Indeo2TestBitWriter();

    bits.WriteCode(_CODE_DELTA_1, _CODE_LENGTH);
    _WriteRuns(bits, 3);
    _WriteSkippedLines(bits, 3);
    _WriteSkippedChroma(bits);

    decoder.TryDecode(new(0, _Frame(bits, intra: true)), out _);

    var luma = decoder.Planes.Luma;
    for (var y = 1; y < 4; ++y)
      Assert.That(luma.Skip(y * 8).Take(8), Is.EqualTo(luma.Take(8)).AsCollection, $"line {y}");
  }

  [Test]
  [Category("Unit")]
  public void AnIntraFramesLaterLinesAddTheTableEntryAsADifference() {
    var decoder = Indeo2VideoDecoder.Create(_Stream("RT21", 8, 4));
    var bits = new Indeo2TestBitWriter();

    bits.WriteCode(_CODE_DELTA_1, _CODE_LENGTH);
    _WriteRuns(bits, 3);

    // Second line: the same code again, now meaning a difference of 0x84 - 128 = +4.
    bits.WriteCode(_CODE_DELTA_1, _CODE_LENGTH);
    _WriteRuns(bits, 3);
    _WriteSkippedLines(bits, 2);
    _WriteSkippedChroma(bits);

    decoder.TryDecode(new(0, _Frame(bits, intra: true)), out _);

    Assert.That(decoder.Planes.Luma[8], Is.EqualTo(0x88));
    Assert.That(decoder.Planes.Luma[9], Is.EqualTo(0x88));
  }

  // ============================================================================================
  // Inter frames
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnInterFramesDeltaIsThreeQuartersOfTheTablesOwn() {
    var decoder = Indeo2VideoDecoder.Create(_Stream("RT21", 8, 4));

    var intra = new Indeo2TestBitWriter();
    intra.WriteCode(_CODE_DELTA_1, _CODE_LENGTH);
    _WriteRuns(intra, 3);
    _WriteSkippedLines(intra, 3);
    _WriteSkippedChroma(intra);
    decoder.TryDecode(new(0, _Frame(intra, intra: true)), out _);

    var inter = new Indeo2TestBitWriter();
    inter.WriteCode(_CODE_DELTA_1, _CODE_LENGTH);
    _WriteRuns(inter, 3);
    _WriteSkippedLines(inter, 3);
    _WriteSkippedChroma(inter);
    decoder.TryDecode(new(0, _Frame(inter, intra: false)), out _);

    // ((0x84 - 128) * 3) >> 2 is 3, added to the 0x84 the intra frame left there.
    Assert.That(decoder.Planes.Luma[0], Is.EqualTo(0x87));
    Assert.That(decoder.Planes.Luma[1], Is.EqualTo(0x87));
    Assert.That(decoder.Planes.Luma[2], Is.EqualTo(0x80), "a run in an inter frame changes nothing");
  }

  // ============================================================================================
  // The tables, which no file carries
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheHuffmanTableHolds143Codes() {
    Assert.That(Indeo2Tables.Codes, Has.Length.EqualTo(Indeo2Tables.CODE_COUNT));
    Assert.That(Indeo2Tables.Codes[0], Is.EqualTo(new Indeo2Tables.CodeEntry(0x01, 3)));
    Assert.That(Indeo2Tables.Codes[^1], Is.EqualTo(new Indeo2Tables.CodeEntry(0x7F, 14)));
  }

  [Test]
  [Category("Unit")]
  public void ThereAreFourDeltaTablesOf256Entries() {
    Assert.That(Indeo2Tables.Deltas, Has.Length.EqualTo(4));
    foreach (var table in Indeo2Tables.Deltas)
      Assert.That(table, Has.Length.EqualTo(256));

    // Each table opens on the neutral pair and closes on it again, and the four differ from the third
    // entry onwards — that is the whole of what choosing a table does.
    Assert.That(Indeo2Tables.Deltas.Select(t => t[2]), Is.EqualTo(new byte[] { 0x84, 0x85, 0x86, 0x87 }).AsCollection);
    Assert.That(Indeo2Tables.Deltas.Select(t => t[0]), Is.EqualTo(new byte[] { 0x80, 0x80, 0x80, 0x80 }).AsCollection);
    Assert.That(Indeo2Tables.Deltas.Select(t => t[255]), Is.EqualTo(new byte[] { 0x80, 0x80, 0x80, 0x80 }).AsCollection);
  }

  // ============================================================================================
  // A real file, against ffmpeg's own decode of it
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void SixFramesOfARealFileMatchFfmpegSampleForSample() {
    var (stream, packets) = IndeoFixtures.Read(IndeoFixtures.INDEO_2);
    var expected = IndeoFixtures.ExpectedFrames(IndeoFixtures.INDEO_2);

    Assert.That(stream.Codec.ToString(), Is.EqualTo("RT21"));
    Assert.That(packets, Has.Count.EqualTo(expected.Count));

    var decoder = Indeo2VideoDecoder.Create(stream);
    var planes = decoder.Planes;
    var actual = new List<string>();

    foreach (var packet in packets) {
      decoder.TryDecode(packet, out _);
      actual.Add(IndeoFixtures.Checksum(
        planes.Luma[..(planes.Width * planes.Height)],
        planes.Cb[..(planes.ChromaWidth * planes.ChromaHeight)],
        planes.Cr[..(planes.ChromaWidth * planes.ChromaHeight)]));
    }

    Assert.That(actual, Is.EqualTo(expected).AsCollection);
  }

  // ============================================================================================
  // Building frames
  // ============================================================================================

  /// <summary>Wraps a bit stream in the 48-byte header a frame carries in front of it.</summary>
  private static byte[] _Frame(Indeo2TestBitWriter bits, bool intra) {
    var body = bits.ToArray();
    var frame = new byte[48 + Math.Max(body.Length, 1)];
    frame[18] = intra ? (byte)1 : (byte)0;
    frame[0x22] = 0; // Delta table 0 for both luminance and chrominance.
    body.CopyTo(frame, 48);

    return frame;
  }

  private static void _WriteRuns(Indeo2TestBitWriter bits, int count) {
    for (var i = 0; i < count; ++i)
      bits.WriteCode(_CODE_RUN_OF_TWO, _CODE_LENGTH);
  }

  private static void _WriteSkippedLines(Indeo2TestBitWriter bits, int lines) {
    for (var y = 0; y < lines; ++y)
      _WriteRuns(bits, 4);
  }

  /// <summary>Both chrominance planes of an 8x4 picture, one code each — they are two samples wide.</summary>
  private static void _WriteSkippedChroma(Indeo2TestBitWriter bits) => _WriteRuns(bits, 2);

  private static MediaStreamInfo _Stream(string codec, int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
  };
}

/// <summary>
/// Writes Indeo 2's Huffman codes the way a stream carries them: least-significant-bit first, with the
/// leading bit of a code written first.
/// </summary>
internal sealed class Indeo2TestBitWriter {

  private readonly List<byte> _bytes = [];
  private int _bitCount;

  internal void WriteCode(int code, int length) {
    for (var i = length - 1; i >= 0; --i)
      this._WriteBit((code >> i) & 1);
  }

  internal byte[] ToArray() => this._bytes.ToArray();

  private void _WriteBit(int bit) {
    if ((this._bitCount & 7) == 0)
      this._bytes.Add(0);

    if (bit != 0)
      this._bytes[^1] |= (byte)(1 << (this._bitCount & 7));

    ++this._bitCount;
  }
}
