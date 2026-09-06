using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Indeo 3's two frame headers, the forms it refuses, its quantisation tables, and six frames of a real
/// file measured against ffmpeg's own decode of them.
/// </summary>
/// <remarks>
/// The bitstream itself is not built here. An Indeo 3 frame is a binary tree interleaved with the byte
/// runs its own leaves name, and a frame small enough to write out by hand would exercise one leaf and
/// none of the interleaving that is the hard part of the format — so what the decode does is settled by
/// the real file, frame for frame against a second implementation, and what is settled here is
/// everything around it: which headers are accepted, which forms are refused and by what name, and that
/// the tables no file carries hold the numbers they are supposed to.
/// </remarks>
[TestFixture]
public sealed class Indeo3VideoDecoderTests {

  /// <summary>The four bytes an operating-system header's checksum is taken against.</summary>
  private const uint _HEADER_ID = 0x46524D48;

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheIv32CodeIsTaken() => Assert.That(Indeo3VideoDecoder.Accepts(_Stream("IV32")), Is.True);

  [Test]
  [Category("Unit")]
  public void TheIv31CodeIsTaken() => Assert.That(Indeo3VideoDecoder.Accepts(_Stream("IV31")), Is.True);

  [Test]
  [Category("Unit")]
  public void TheQuickTimeSpellingIsTaken() => Assert.That(Indeo3VideoDecoder.Accepts(_Stream("iv32")), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() => Assert.That(Indeo3VideoDecoder.Accepts(_Stream("RT21")), Is.False);

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var stream = _Stream("IV32");
    stream = new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = stream.Codec, Width = 160, Height = 120 };

    Assert.That(Indeo3VideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream("IV32");

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Intel Indeo 3"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Indeo3VideoDecoder>());
  }

  // ============================================================================================
  // Creation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureWithNoPixelsRefuses()
    => Assert.Throws<InvalidDataException>(() => Indeo3VideoDecoder.Create(_Stream("IV32", 0, 0)));

  [Test]
  [Category("Unit")]
  public void APictureSmallerThanTheCodecDefinesRefuses()
    => Assert.Throws<NotSupportedException>(() => Indeo3VideoDecoder.Create(_Stream("IV32", 8, 8)));

  [Test]
  [Category("Unit")]
  public void APictureLargerThanTheCodecDefinesRefuses()
    => Assert.Throws<NotSupportedException>(() => Indeo3VideoDecoder.Create(_Stream("IV32", 800, 600)));

  // ============================================================================================
  // The two frame headers
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AHeaderThatFailsItsOwnChecksumRefuses() {
    var decoder = Indeo3VideoDecoder.Create(_Stream("IV32"));
    var frame = _Frame();
    frame[8] ^= 0xFF;

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, frame), out _));
    Assert.That(failure!.Message, Does.Contain("checksum"));
  }

  [Test]
  [Category("Unit")]
  public void AnUnknownBitstreamVersionRefuses() {
    var decoder = Indeo3VideoDecoder.Create(_Stream("IV32"));
    var frame = _Frame(version: 33);

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, frame), out _));
    Assert.That(failure!.Message, Does.Contain("version 33"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameShorterThanItsOwnHeadersRefuses() {
    var decoder = Indeo3VideoDecoder.Create(_Stream("IV32"));

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[32]), out _));
  }

  [Test]
  [Category("Unit")]
  public void ASyncFrameMakesNoPicture() {
    var decoder = Indeo3VideoDecoder.Create(_Stream("IV32"));

    // A frame stating sixteen bytes of data states nothing but its own header.
    Assert.That(decoder.TryDecode(new(0, _Frame(dataBits: 16 * 8)), out var picture), Is.False);
    Assert.That(picture, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void EightBitSamplesRefuse() {
    var decoder = Indeo3VideoDecoder.Create(_Stream("IV32"));

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _Frame(flags: 1 << 1)), out _));
    Assert.That(failure!.Message, Does.Contain("eight bits a sample"));
  }

  [Test]
  [Category("Unit")]
  public void HalfSampleMotionVectorsRefuse() {
    var decoder = Indeo3VideoDecoder.Create(_Stream("IV32"));

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _Frame(flags: 1 << 5)), out _));
    Assert.That(failure!.Message, Does.Contain("half-sample"));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeTheCodecCannotHoldRefuses() {
    var decoder = Indeo3VideoDecoder.Create(_Stream("IV32"));

    // Not a whole number of 4x4 blocks, and a size change is the only time the codec checks that.
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _Frame(width: 158)), out _));
    Assert.That(failure!.Message, Does.Contain("158x120"));
  }

  [Test]
  [Category("Unit")]
  public void PlaneOffsetsOutsideTheFrameRefuse() {
    var decoder = Indeo3VideoDecoder.Create(_Stream("IV32"));

    // Inside the headers rather than after them: the first plane may not start before byte 48.
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _Frame(lumaOffset: 40)), out _));
    Assert.That(failure!.Message, Does.Contain("plane offsets"));
  }

  // ============================================================================================
  // The tables, which no file carries
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ThereAreTwentyFourQuantisationTables() {
    Assert.That(Indeo3Tables.Tables, Has.Length.EqualTo(Indeo3Tables.TABLE_COUNT));
    Assert.That(Indeo3Tables.Tables[0].DyadCount, Is.EqualTo(195));
    Assert.That(Indeo3Tables.Tables[0].QuadDivisor, Is.EqualTo(7));
    Assert.That(Indeo3Tables.Tables[7].DyadCount, Is.EqualTo(77));
    Assert.That(Indeo3Tables.Tables[16].DyadCount, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void TheCoarsestChrominanceTableAnswersToTheFourIndicesAboveIt() {
    for (var i = 21; i < Indeo3Tables.TABLE_COUNT; ++i)
      Assert.That(Indeo3Tables.Tables[i], Is.SameAs(Indeo3Tables.Tables[20]), $"table {i}");
  }

  [Test]
  [Category("Unit")]
  public void ACompressedEntryExpandsToItsPairAndTheThreeThatFollowFromIt() {
    // The first set opens PD(0,0), E2(2,2), E4(-1,3): one entry, then a pair and its negation, then a
    // pair, its negation, the pair reversed and that reversed pair negated.
    var deltas = Indeo3Tables.Tables[0].Deltas;

    Assert.That(deltas[0], Is.EqualTo(_Pack(0, 0)));
    Assert.That(deltas[1], Is.EqualTo(_Pack(2, 2)));
    Assert.That(deltas[2], Is.EqualTo(_Pack(-2, -2)));
    Assert.That(deltas[3], Is.EqualTo(_Pack(-1, 3)));
    Assert.That(deltas[4], Is.EqualTo(_Pack(1, -3)));
    Assert.That(deltas[5], Is.EqualTo(_Pack(3, -1)));
    Assert.That(deltas[6], Is.EqualTo(_Pack(-3, 1)));
  }

  [Test]
  [Category("Unit")]
  public void TheWideFormOfAnEntryIsTheSameTwoDeltasDoubledUp() {
    var wide = Indeo3Tables.Tables[0].WideDeltas;

    Assert.That(wide[1], Is.EqualTo(_PackWide(2, 2)));
    Assert.That(wide[3], Is.EqualTo(_PackWide(-1, 3)));
  }

  [Test]
  [Category("Unit")]
  public void ATableShorterThanItsOwnLengthIsPaddedWithNeutralPairs() {
    // The coarsest chrominance table states 79 entries and the format writes only thirteen of them;
    // a cell may still name any of the 79, and the rest are pairs of zero.
    var table = Indeo3Tables.Tables[20];

    Assert.That(table.Deltas, Has.Length.EqualTo(79));
    Assert.That(table.Deltas[12], Is.EqualTo(_Pack(-46, -46)));
    Assert.That(table.Deltas[13], Is.EqualTo(0));
    Assert.That(table.Deltas[78], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void TheRequantisationTableKeepsItsHandSetEntries() {
    Assert.Multiple(() => {
      // The rows the formula runs past 127 on.
      Assert.That(Indeo3Tables.Requantise[0][127], Is.EqualTo(126));
      Assert.That(Indeo3Tables.Requantise[2][127], Is.EqualTo(124));
      Assert.That(Indeo3Tables.Requantise[6][127], Is.EqualTo(120));

      // The two entries Intel's own decoders differ from the formula on. Matching them is the point:
      // every file was encoded against a decoder that held these.
      Assert.That(Indeo3Tables.Requantise[1][7], Is.EqualTo(10));
      Assert.That(Indeo3Tables.Requantise[4][8], Is.EqualTo(10));

      // And one the formula does give, so that the patches above are not the whole test.
      Assert.That(Indeo3Tables.Requantise[0][10], Is.EqualTo(10));
    });
  }

  // ============================================================================================
  // A real file, against ffmpeg's own decode of it
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void SixFramesOfARealFileMatchFfmpegSampleForSample() {
    var (stream, packets) = IndeoFixtures.Read(IndeoFixtures.INDEO_3);
    var expected = IndeoFixtures.ExpectedFrames(IndeoFixtures.INDEO_3);

    Assert.That(stream.Codec.ToString(), Is.EqualTo("IV32"));
    Assert.That(packets, Has.Count.EqualTo(expected.Count));

    var decoder = Indeo3VideoDecoder.Create(stream);
    var planes = decoder.Planes;
    var actual = new List<string>();

    foreach (var packet in packets) {
      Assert.That(decoder.TryDecode(packet, out _), Is.True);
      actual.Add(IndeoFixtures.Checksum(planes.Luma, planes.Cb, planes.Cr));
    }

    Assert.That(actual, Is.EqualTo(expected).AsCollection);
  }

  // ============================================================================================
  // Building headers
  // ============================================================================================

  /// <summary>
  /// A frame carrying both headers and three plane offsets that lie inside it, and nothing else.
  /// </summary>
  /// <remarks>
  /// Enough to reach every check the headers make, and no further: the planes it points at hold zeros,
  /// so any test using this must be one that refuses before a plane is read.
  /// </remarks>
  private static byte[] _Frame(
    int version = 32, int flags = 0, int dataBits = 256 * 8, int width = 160, int height = 120,
    int lumaOffset = 48) {
    var frame = new byte[16 + 256];
    var dataSize = (uint)((dataBits + 7) >> 3);

    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(12), dataSize);
    BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8), dataSize ^ _HEADER_ID);

    var header = frame.AsSpan(16);
    BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)version);
    BinaryPrimitives.WriteUInt16LittleEndian(header[2..], (ushort)flags);
    BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)dataBits);
    BinaryPrimitives.WriteUInt16LittleEndian(header[12..], (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)width);
    BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)lumaOffset);
    BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)(lumaOffset + 16));
    BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)(lumaOffset + 32));

    return frame;
  }

  /// <summary>Two deltas as one 16-bit word, the way a 4-wide block adds them.</summary>
  private static short _Pack(int a, int b) => (short)((b << 8) + a);

  /// <summary>The same two deltas doubled up into 32 bits, the way an 8-wide block adds them.</summary>
  private static int _PackWide(int a, int b) => (b << 24) + (b << 16) + (a << 8) + a;

  private static MediaStreamInfo _Stream(string codec, int width = 160, int height = 120) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
  };
}
