using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Jpeg;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Matroska.Tests;

/// <summary>
/// The Matroska reader's behaviour — Matroska and WebM being one format under two document types.
/// </summary>
/// <remarks>
/// Everything asserted here was first measured against ffprobe, on files ffmpeg wrote where ffmpeg
/// writes them and on files assembled by hand where it does not. ffmpeg's Matroska muxer emits no
/// laced block, no element it does not know the length of outside its live mode, and no track
/// carrying a <c>BITMAPINFOHEADER</c> unless asked; each of those forms was therefore built by hand,
/// put past ffprobe, and only written as a test once ffprobe read the same packets out of it.
/// <para/>
/// The reader is a demuxer and nothing else, so most of what is tested is packets: how many, how big,
/// which stream, and when each is due. The tests that decode exist to prove the seam rather than the
/// codec — a Matroska of Motion JPEG reaches the same decoder an AVI and a MOV of it do, and none of
/// the three containers knows anything about JPEG.
/// </remarks>
[TestFixture]
public sealed class MatroskaReaderTests {

  private const int _WIDTH = 8;
  private const int _HEIGHT = 4;

  // ------------------------------------------------------------------------------------------
  // Opening
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MatroskaReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MatroskaReader.FromBytes(new byte[3]));

  [Test]
  [Category("Unit")]
  public void WithoutAnEbmlHeader_IsRefused() {
    var document = _Simple();

    // The header's own identifier, broken. What is left still holds a perfectly good segment, and
    // the point is that a document is not read without one.
    document[0] = 0x2A;

    Assert.Throws<InvalidDataException>(() => MatroskaReader.FromBytes(document));
  }

  [TestCase("matroska")]
  [TestCase("webm")]
  [Category("Unit")]
  public void BothDocumentTypesAreOneFormat(string docType) {
    // WebM is Matroska with a shorter list of codecs allowed inside it, and which codecs are allowed
    // is the business of whoever is asked for a decoder. The bytes are the same bytes.
    var container = MatroskaReader.FromBytes(
      MatroskaTestContainer.Build(
        [new MatroskaTestTrack()],
        [new MatroskaTestCluster { Blocks = [_Block(0, _Payload(1, 6))] }],
        docType: docType));

    Assert.That(container.DocType, Is.EqualTo(docType));
    Assert.That(MatroskaContainer.ReadPackets(container).Count(), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void AnEbmlDocumentOfAnotherKind_IsRefusedByName() {
    // EBML carries a good deal that is not video, and the identifiers this reader looks for mean
    // nothing in those — it would find no tracks and report a container of no streams, which is
    // indistinguishable from a Matroska file that genuinely holds none.
    var document = MatroskaTestContainer.Build([new MatroskaTestTrack()], docType: "dvbsub");

    var failure = Assert.Throws<NotSupportedException>(() => MatroskaReader.FromBytes(document));
    Assert.That(failure!.Message, Does.Contain("dvbsub"));
  }

  [Test]
  [Category("Unit")]
  public void WithoutTracks_IsRefused() {
    var document = MatroskaTestContainer.Build([], withoutTracks: true);

    var failure = Assert.Throws<InvalidDataException>(() => MatroskaReader.FromBytes(document));
    Assert.That(failure!.Message, Does.Contain("Tracks"));
  }

  [Test]
  [Category("Unit")]
  public void ATracksElementWithNoEntry_IsRefused()
    => Assert.Throws<InvalidDataException>(() => MatroskaReader.FromBytes(MatroskaTestContainer.Build([])));

  [Test]
  [Category("Unit")]
  public void ATrackWithoutACodecId_IsRefused() {
    var document = MatroskaTestContainer.Build([new MatroskaTestTrack { CodecId = null }]);

    var failure = Assert.Throws<InvalidDataException>(() => MatroskaReader.FromBytes(document));
    Assert.That(failure!.Message, Does.Contain("CodecID"));
  }

  [Test]
  [Category("Unit")]
  public void ATrackWithoutANumber_IsRefused() {
    // A block names its track by number. An entry without one can never be the track a block belongs
    // to, so every block of it would be silently dropped.
    var document = MatroskaTestContainer.Build([new MatroskaTestTrack { WithoutNumber = true }]);

    var failure = Assert.Throws<InvalidDataException>(() => MatroskaReader.FromBytes(document));
    Assert.That(failure!.Message, Does.Contain("TrackNumber"));
  }

  [Test]
  [Category("Unit")]
  public void ACompressedTrack_IsRefusedByName() {
    // Header stripping removes bytes the decoder needs and leaves a payload that still looks entirely
    // plausible. Handing one on would come back as a picture full of noise with nothing in the file
    // to point at, so it is refused instead.
    var document = MatroskaTestContainer.Build([new MatroskaTestTrack { CompressionAlgorithm = 3 }]);

    var failure = Assert.Throws<NotSupportedException>(() => MatroskaReader.FromBytes(document));
    Assert.That(failure!.Message, Does.Contain("ContentCompression"));
    Assert.That(failure.Message, Does.Contain("3"));
  }

  [Test]
  [Category("Unit")]
  public void AnEncryptedTrack_IsRefusedByName() {
    var document = MatroskaTestContainer.Build([new MatroskaTestTrack { Encrypted = true }]);

    var failure = Assert.Throws<NotSupportedException>(() => MatroskaReader.FromBytes(document));
    Assert.That(failure!.Message, Does.Contain("ContentEncryption"));
  }

  [Test]
  [Category("Unit")]
  public void TheSignatureIsTheEbmlMagicAndNothingElse() {
    // The DocType that says which EBML document this is lives inside the header rather than at a
    // fixed offset, so the signature claims EBML and the document type is decided when it is opened.
    var document = _Simple();

    Assert.That(VideoFormatRegistry.Detect(document), Is.EqualTo(VideoFormat.Matroska));
    Assert.That(MatroskaContainer.MatchesSignature(document), Is.True);
    Assert.That(MatroskaContainer.MatchesSignature(new byte[] { 0x1A, 0x45, 0xDF }), Is.Null);
    Assert.That(MatroskaContainer.MatchesSignature("RIFF"u8), Is.Null);
  }

  [TestCase(".mkv")]
  [TestCase(".mka")]
  [TestCase(".mks")]
  [TestCase(".mk3d")]
  [TestCase(".webm")]
  [Category("Unit")]
  public void EveryNameTheOneFormatGoesUnder_ReachesThisReader(string extension)
    => Assert.That(VideoFormatRegistry.ByExtension(extension), Does.Contain(VideoFormat.Matroska));

  // ------------------------------------------------------------------------------------------
  // EBML
  // ------------------------------------------------------------------------------------------

  [TestCase(0)]
  [TestCase(2)]
  [TestCase(4)]
  [TestCase(8)]
  [Category("Unit")]
  public void ALengthWrittenWiderThanItNeeds_MeansTheSameThing(int sizeWidth) {
    // A length drops its marker bit once read, so the same number written in one byte and in eight is
    // the same number. Getting that bit wrong shifts every later read by a byte and the file reads
    // like corruption rather than like a parser fault, which is why every width is checked.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [new MatroskaTestCluster { Blocks = [_Block(0, _Payload(1, 6)), _Block(100, _Payload(2, 9))] }],
      sizeWidth: sizeWidth);

    var packets = _Packets(document);

    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 6, 9 }));
    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 100 }));
  }

  [Test]
  [Category("Unit")]
  public void ElementsThisReaderHasNeverHeardOf_AreWalkedPast() {
    // Not leniency: it is how EBML is specified to be read. Void, CRC-32 and everything a later
    // version of the schema adds sit among the elements a reader knows, and one that stopped at the
    // first unfamiliar identifier would not get past the first cluster of a file ffmpeg wrote.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [new MatroskaTestCluster { Blocks = [_Block(0, _Payload(1, 6))] }],
      padding: 64);

    Assert.That(_Packets(document), Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ASegmentTheWriterStatedNoLengthFor_RunsToTheEndOfTheFile() {
    // What ffmpeg's live mode writes, because a segment being sent down a pipe cannot know its own
    // size. ffprobe reads such a file's packets exactly as it reads the same content with a length.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [new MatroskaTestCluster { Blocks = [_Block(0, _Payload(1, 6)), _Block(100, _Payload(2, 7))] }],
      unknownSizeSegment: true);

    Assert.That(_Packets(document).Select(p => p.Data.Length), Is.EqualTo(new[] { 6, 7 }));
  }

  [Test]
  [Category("Unit")]
  public void AClusterTheWriterStatedNoLengthFor_EndsWhereTheNextOneBegins() {
    // A cluster with no length is closed by the first element that cannot be inside it, which for a
    // segment's children is the next cluster. There is nothing in the file that says where it stops
    // other than that.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [
        new MatroskaTestCluster { UnknownSize = true, Blocks = [_Block(0, _Payload(1, 6))] },
        new MatroskaTestCluster { UnknownSize = true, Timestamp = 100, Blocks = [_Block(0, _Payload(2, 7))] },
      ],
      unknownSizeSegment: true);

    var packets = _Packets(document);

    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 6, 7 }));
    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 100 }));
  }

  // ------------------------------------------------------------------------------------------
  // Streams
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void EveryTrackIsReported_NotOnlyTheOnesAnythingDecodes() {
    // A stream's number is its position among all of them, so leaving the sound out would make the
    // pictures go looking under the wrong one — and a muxer copying the file across has to carry the
    // sound through even when nothing here decodes it.
    var streams = _Streams(MatroskaTestContainer.Build([
      new MatroskaTestTrack { Number = 1, Type = 1, CodecId = "V_VP9" },
      new MatroskaTestTrack { Number = 2, Type = 2, CodecId = "A_OPUS", WithoutVideo = true },
      new MatroskaTestTrack { Number = 3, Type = 0x11, CodecId = "S_TEXT/UTF8", WithoutVideo = true },
      new MatroskaTestTrack { Number = 4, Type = 0x21, CodecId = "B_META", WithoutVideo = true },
    ]));

    Assert.That(streams.Select(s => s.Index), Is.EqualTo(new[] { 0, 1, 2, 3 }));
    Assert.That(streams.Select(s => s.Kind), Is.EqualTo(new[] {
      MediaStreamKind.Video, MediaStreamKind.Audio, MediaStreamKind.Subtitle, MediaStreamKind.Data,
    }));
  }

  [Test]
  [Category("Unit")]
  public void ATrackNumberIsNotAStreamIndex() {
    // Track numbers start at one and a file may leave gaps in them — removing a track leaves the
    // others where they were. A reader that took the number for the index would attribute every
    // packet of the second track to a stream that is not there.
    var document = MatroskaTestContainer.Build(
      [
        new MatroskaTestTrack { Number = 3 },
        new MatroskaTestTrack { Number = 7, Type = 2, CodecId = "A_OPUS", WithoutVideo = true },
      ],
      [
        new MatroskaTestCluster {
          Blocks = [
            new MatroskaTestBlock { Track = 7, Frames = [_Payload(1, 5)] },
            new MatroskaTestBlock { Track = 3, Frames = [_Payload(2, 6)] },
          ],
        },
      ]);

    var packets = _Packets(document);

    Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 1, 0 }));
    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 5, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void ABlockOfATrackThatWasNeverDeclared_IsSkipped() {
    // It belongs to nothing. Skipped rather than refused, because that is what ffmpeg does with one
    // and because the rest of the file is perfectly readable without it.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack { Number = 1 }],
      [
        new MatroskaTestCluster {
          Blocks = [
            new MatroskaTestBlock { Track = 1, Frames = [_Payload(1, 5)] },
            new MatroskaTestBlock { Track = 9, Frames = [_Payload(2, 6)] },
          ],
        },
      ]);

    Assert.That(_Packets(document).Select(p => p.Data.Length), Is.EqualTo(new[] { 5 }));
  }

  [Test]
  [Category("Unit")]
  public void AMatroskaCodecIsNamedRatherThanCoded() {
    // There is no four-character code in the file to report. Deriving one would put a number in the
    // stream's description that is in no file, and a refusal naming 0x00000000 tells nobody which
    // codec is missing.
    var stream = _Streams(MatroskaTestContainer.Build([new MatroskaTestTrack { CodecId = "V_VP9" }]))[0];

    Assert.That(stream.CodecId, Is.EqualTo("V_VP9"));
    Assert.That(stream.Codec, Is.EqualTo(CodecTag.None));
  }

  [Test]
  [Category("Unit")]
  public void AVideoForWindowsTrackDoesCarryAFourCharacterCode() {
    // The one CodecID that does. Its private data is a BITMAPINFOHEADER — the same structure an AVI's
    // strf is — and biCompression is a real code sitting in the file; ffprobe reads such a track's tag
    // as MJPG where the same picture written as V_MJPEG gets no tag at all.
    var private_ = MatroskaTestContainer.BitmapInfoHeader("MJPG", _WIDTH, _HEIGHT, 24);
    var stream = _Streams(MatroskaTestContainer.Build([
      new MatroskaTestTrack { CodecId = "V_MS/VFW/FOURCC", CodecPrivate = private_ },
    ]))[0];

    Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("MJPG")));
    Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
    Assert.That(stream.CodecPrivateData.ToArray(), Is.EqualTo(private_));
  }

  [Test]
  [Category("Unit")]
  public void CodecPrivateDataCrossesVerbatim() {
    // The demuxer's only job is to find the bytes and say which stream they belong to. What is in
    // them is defined by the codec, and a container that parsed it would be doing the codec's work.
    var configuration = _Payload(9, 46);
    var stream = _Streams(MatroskaTestContainer.Build([
      new MatroskaTestTrack { CodecId = "V_MPEG4/ISO/AVC", CodecPrivate = configuration },
    ]))[0];

    Assert.That(stream.CodecPrivateData.ToArray(), Is.EqualTo(configuration));
  }

  [Test]
  [Category("Unit")]
  public void ThePictureSizeIsWhatTheVideoElementStates() {
    var stream = _Streams(MatroskaTestContainer.Build([new MatroskaTestTrack { Width = 64, Height = 48 }]))[0];

    Assert.That(stream.Width, Is.EqualTo(64));
    Assert.That(stream.Height, Is.EqualTo(48));
  }

  [TestCase(1_000_000L, 1L, 1000L)]
  [TestCase(100_000_000L, 1L, 10L)]
  [TestCase(1L, 1L, 1_000_000_000L)]
  [Category("Unit")]
  public void TheTimeBaseIsOneTickOfTheSegmentsOwnClock(long scale, long numerator, long denominator) {
    // Reduced, the way ffprobe reports it: a scale of 1 000 000 ns against a second is 1/1000 and not
    // 1000000/1000000000, and the unreduced form reads as nothing at all.
    var stream = _Streams(MatroskaTestContainer.Build([new MatroskaTestTrack()], timestampScale: scale))[0];

    Assert.That(stream.TimeBase, Is.EqualTo(new Rational(numerator, denominator)));
  }

  [Test]
  [Category("Unit")]
  public void TheFrameRateIsTheReciprocalOfTheDefaultDuration() {
    // DefaultDuration is nanoseconds a frame whatever the segment's scale, so 100 000 000 is the 10/1
    // ffprobe reports for a ten-frame-a-second file.
    var stream = _Streams(MatroskaTestContainer.Build([new MatroskaTestTrack { DefaultDuration = 100_000_000 }]))[0];

    Assert.That(stream.FrameRate, Is.EqualTo(new Rational(10, 1)));
  }

  [Test]
  [Category("Unit")]
  public void WithoutADefaultDuration_NoFrameRateIsInvented() {
    var stream = _Streams(MatroskaTestContainer.Build([new MatroskaTestTrack { DefaultDuration = 0 }]))[0];

    Assert.That(stream.FrameRate.IsKnown, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ATrackWithNoLanguageIsInEnglish() {
    // A peculiar default for a format used everywhere, and nonetheless what the specification says.
    // Measured: a file built with no Language element at all is reported by ffprobe as language=eng,
    // so reporting nothing would disagree with every other tool about a file that says nothing.
    var stream = _Streams(MatroskaTestContainer.Build([new MatroskaTestTrack { Language = null }]))[0];

    Assert.That(stream.Language, Is.EqualTo("eng"));
  }

  [Test]
  [Category("Unit")]
  public void TheBcp47LanguageOutranksTheOlderOne() {
    var stream = _Streams(MatroskaTestContainer.Build([
      new MatroskaTestTrack { Language = "ger", LanguageBcp47 = "de-AT" },
    ]))[0];

    Assert.That(stream.Language, Is.EqualTo("de-AT"));
  }

  [Test]
  [Category("Unit")]
  public void ATrackCarriesTheNameTheWriterGaveIt() {
    var stream = _Streams(MatroskaTestContainer.Build([new MatroskaTestTrack { Name = "Main camera" }]))[0];

    Assert.That(stream.Name, Is.EqualTo("Main camera"));
  }

  // ------------------------------------------------------------------------------------------
  // Packets
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void ABlocksTimestampIsItsClustersPlusItsOwnOffset() {
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [
        new MatroskaTestCluster { Timestamp = 0, Blocks = [_Block(0, _Payload(1, 4)), _Block(100, _Payload(2, 4))] },
        new MatroskaTestCluster { Timestamp = 300, Blocks = [_Block(0, _Payload(3, 4)), _Block(100, _Payload(4, 4))] },
      ]);

    Assert.That(_Packets(document).Select(p => p.PresentationTimestamp),
      Is.EqualTo(new long?[] { 0, 100, 300, 400 }));
  }

  [Test]
  [Category("Unit")]
  public void ABlockOffsetIsSignedAndMayPrecedeItsOwnCluster() {
    // Sixteen bits and signed, which is not a formality. Read unsigned, a block stored at -200 would
    // land some sixty-five seconds into the future rather than a fifth of a second into the past.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [
        new MatroskaTestCluster {
          Timestamp = 200,
          Blocks = [_Block(-200, _Payload(1, 4)), _Block(-100, _Payload(2, 4)), _Block(0, _Payload(3, 4))],
        },
      ]);

    Assert.That(_Packets(document).Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 100, 200 }));
  }

  [Test]
  [Category("Unit")]
  public void PacketsComeOutInTheOrderTheFileStoresThem() {
    // Storage order, which for a file with sound is the interleaving the writer chose rather than one
    // whole track followed by another. Nothing has to be merged to recover it — a cluster already
    // holds the blocks of every track in the order they are due.
    var document = MatroskaTestContainer.Build(
      [
        new MatroskaTestTrack { Number = 1 },
        new MatroskaTestTrack { Number = 2, Type = 2, CodecId = "A_VORBIS", WithoutVideo = true, DefaultDuration = 0 },
      ],
      [
        new MatroskaTestCluster {
          Blocks = [
            new MatroskaTestBlock { Track = 2, Relative = 0, Frames = [_Payload(1, 3)] },
            new MatroskaTestBlock { Track = 1, Relative = 0, Frames = [_Payload(2, 4)] },
            new MatroskaTestBlock { Track = 2, Relative = 23, Frames = [_Payload(3, 5)] },
            new MatroskaTestBlock { Track = 1, Relative = 100, Frames = [_Payload(4, 6)] },
          ],
        },
      ]);

    var packets = _Packets(document);

    Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 1, 0, 1, 0 }));
    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 0, 23, 100 }));
  }

  [Test]
  [Category("Unit")]
  public void OneStreamCanBeWalkedOnItsOwn() {
    var document = MatroskaTestContainer.Build(
      [
        new MatroskaTestTrack { Number = 1 },
        new MatroskaTestTrack { Number = 2, Type = 2, CodecId = "A_VORBIS", WithoutVideo = true },
      ],
      [
        new MatroskaTestCluster {
          Blocks = [
            new MatroskaTestBlock { Track = 2, Frames = [_Payload(1, 3)] },
            new MatroskaTestBlock { Track = 1, Frames = [_Payload(2, 4)] },
            new MatroskaTestBlock { Track = 1, Relative = 100, Frames = [_Payload(3, 5)] },
          ],
        },
      ]);

    var container = MatroskaReader.FromBytes(document);

    Assert.That(MatroskaContainer.ReadPackets(container, 0).Select(p => p.Data.Length), Is.EqualTo(new[] { 4, 5 }));
    Assert.That(MatroskaContainer.ReadPackets(container, 1).Select(p => p.Data.Length), Is.EqualTo(new[] { 3 }));
    Assert.That(MatroskaContainer.ReadPackets(container, 2), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void APacketIsAWindowOntoTheFileRatherThanACopy() {
    // A demuxer walking a film must not leave a copy of it behind. The frames are the file's own
    // bytes, so a packet taken from the wrong offset shows up as the wrong payload rather than as a
    // length that happens to match.
    var frame = _Payload(5, 12);
    var packets = _Packets(MatroskaTestContainer.Build("V_MJPEG", [frame]));

    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(frame));
  }

  [Test]
  [Category("Unit")]
  public void TheKeyframeFlagOfASimpleBlockIsRead() {
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [
        new MatroskaTestCluster {
          Blocks = [
            new MatroskaTestBlock { Relative = 0, KeyFrame = true, Frames = [_Payload(1, 4)] },
            new MatroskaTestBlock { Relative = 100, KeyFrame = false, Frames = [_Payload(2, 4)] },
          ],
        },
      ]);

    Assert.That(_Packets(document).Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, false }));
  }

  [Test]
  [Category("Unit")]
  public void ABlockInAGroupIsAKeyframeWhenTheGroupNamesNothingItDependsOn() {
    // A Block carries no keyframe flag of its own. ffprobe reports every Vorbis block of a file
    // ffmpeg muxed as a keyframe, and every one of them is a bare Block in a group with a
    // BlockDuration and no ReferenceBlock.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [
        new MatroskaTestCluster {
          Blocks = [
            new MatroskaTestBlock { Relative = 0, InGroup = true, BlockDuration = 3, Frames = [_Payload(1, 4)] },
            new MatroskaTestBlock { Relative = 3, InGroup = true, BlockDuration = 23, Referenced = true, Frames = [_Payload(2, 4)] },
          ],
        },
      ]);

    var packets = _Packets(document);

    Assert.That(packets.Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, false }));
    Assert.That(packets.Select(p => p.Duration), Is.EqualTo(new long?[] { 3, 23 }));
    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void APacketsDurationComesFromTheDefaultDurationWhereTheBlockStatesNone() {
    // Measured: a file whose blocks sit 200 ticks apart but whose DefaultDuration says 100 000 000 ns
    // is reported by ffprobe as packets of duration 100. The declared duration wins over the gap.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack { DefaultDuration = 100_000_000 }],
      [new MatroskaTestCluster { Blocks = [_Block(0, _Payload(1, 4)), _Block(200, _Payload(2, 4))] }]);

    Assert.That(_Packets(document).Select(p => p.Duration), Is.EqualTo(new long?[] { 100, 100 }));
  }

  [Test]
  [Category("Unit")]
  public void WithNothingToSayHowLongAPacketLasts_NoDurationIsInvented() {
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack { DefaultDuration = 0 }],
      [new MatroskaTestCluster { Blocks = [_Block(0, _Payload(1, 4))] }]);

    Assert.That(_Packets(document)[0].Duration, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void ACodecDelayMovesTheTracksTimestampsBack() {
    // ffmpeg writes a CodecDelay of 2 902 494 ns into the Vorbis track of a file it muxes, and
    // ffprobe reports that track's first packet at -3 against a millisecond tick rather than at 0 —
    // rounded rather than truncated, which is the difference between -3 and -2.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack { Type = 2, CodecId = "A_VORBIS", WithoutVideo = true, DefaultDuration = 0, CodecDelay = 2_902_494 }],
      [new MatroskaTestCluster { Blocks = [_Block(0, _Payload(1, 4)), _Block(23, _Payload(2, 4))] }]);

    Assert.That(_Packets(document).Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { -3, 20 }));
  }

  [Test]
  [Category("Unit")]
  public void MatroskaStatesNoDecodeOrder() {
    // It stores presentation timestamps and says nothing about decode order — a codec that reorders
    // frames keeps that in its own bitstream. ffprobe reports the two equal for every Matroska packet
    // measured, and inventing a difference would be inventing an order the file does not describe.
    var packets = _Packets(MatroskaTestContainer.Build("V_MJPEG", [_Payload(1, 4), _Payload(2, 4)]));

    foreach (var packet in packets)
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(packet.PresentationTimestamp));
  }

  // ------------------------------------------------------------------------------------------
  // Lacing
  // ------------------------------------------------------------------------------------------

  [TestCase(TestLacing.Xiph)]
  [TestCase(TestLacing.Ebml)]
  [Category("Unit")]
  public void ALacedBlockIsSeveralPacketsAndNotOne(TestLacing lacing) {
    // A block that packs four frames behind one header is four packets. Reporting it as one would
    // hand a decoder the first frame with the rest stuck to the end of it — and ffprobe reads exactly
    // four packets of exactly these lengths out of the same bytes.
    var frames = new[] { _Payload(1, 369), _Payload(2, 321), _Payload(3, 1044), _Payload(4, 351) };
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack { DefaultDuration = 100_000_000 }],
      [new MatroskaTestCluster { Blocks = [new MatroskaTestBlock { Frames = frames, Lacing = lacing }] }]);

    var packets = _Packets(document);

    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 369, 321, 1044, 351 }));
    for (var i = 0; i < frames.Length; ++i)
      Assert.That(packets[i].Data.ToArray(), Is.EqualTo(frames[i]));
  }

  [Test]
  [Category("Unit")]
  public void AFixedLacedBlockStoresNoSizesBecauseTheFramesAreEqual() {
    var frames = new[] { _Payload(1, 300), _Payload(2, 300), _Payload(3, 300) };
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack { DefaultDuration = 100_000_000 }],
      [new MatroskaTestCluster { Blocks = [new MatroskaTestBlock { Frames = frames, Lacing = TestLacing.Fixed }] }]);

    var packets = _Packets(document);

    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 300, 300, 300 }));
    Assert.That(packets[2].Data.ToArray(), Is.EqualTo(frames[2]));
  }

  [Test]
  [Category("Unit")]
  public void TheFramesOfALaceAreSpreadOverTheBlocksOwnDuration() {
    // ffprobe reports a four-frame lace of a track whose DefaultDuration is 100 000 000 ns as four
    // packets at 0, 100, 200 and 300 of 100 each, not as four packets all due at once.
    var frames = new[] { _Payload(1, 10), _Payload(2, 11), _Payload(3, 12), _Payload(4, 13) };
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack { DefaultDuration = 100_000_000 }],
      [new MatroskaTestCluster { Blocks = [new MatroskaTestBlock { Frames = frames, Lacing = TestLacing.Xiph }] }]);

    var packets = _Packets(document);

    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 100, 200, 300 }));
    Assert.That(packets.Select(p => p.Duration), Is.EqualTo(new long?[] { 100, 100, 100, 100 }));
  }

  [Test]
  [Category("Unit")]
  public void ALaceWhoseDurationDoesNotDivide_KeepsTheRemainder() {
    // A DefaultDuration of 1 451 247 ns against a millisecond tick makes three frames of a lace last
    // 1, 1 and 2 ticks, and ffprobe reports exactly that. Handing every frame the same length would
    // lose the remainder and let the lace drift away from the block after it.
    var frames = new[] { _Payload(1, 10), _Payload(2, 11), _Payload(3, 12) };
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack { DefaultDuration = 1_451_247 }],
      [new MatroskaTestCluster { Blocks = [new MatroskaTestBlock { Frames = frames, Lacing = TestLacing.Xiph }] }]);

    var packets = _Packets(document);

    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 1, 2 }));
    Assert.That(packets.Select(p => p.Duration), Is.EqualTo(new long?[] { 1, 1, 2 }));
  }

  [Test]
  [Category("Unit")]
  public void ALaceWhoseSizesOverrunTheBlock_IsRefused() {
    // A frame cut short is not a frame, and handing one back as though it were is how a demuxer turns
    // a broken file into a broken picture nobody can trace.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [
        new MatroskaTestCluster {
          Blocks = [
            new MatroskaTestBlock {
              Frames = [_Payload(1, 10), _Payload(2, 10)],
              Lacing = TestLacing.Xiph,
              BrokenLaceSizes = [400],
            },
          ],
        },
      ]);

    Assert.Throws<InvalidDataException>(() => _Packets(document));
  }

  [Test]
  [Category("Unit")]
  public void AFileThatStopsInsideABlock_IsRefused() {
    // Part of a frame is not a frame. Handing on the bytes that happen to be there would present a
    // truncated read as a complete one, which is the failure a caller cannot see and cannot trace.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [new MatroskaTestCluster { Blocks = [_Block(0, _Payload(1, 40)), _Block(100, _Payload(2, 40))] }]);

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(document[..^20]));
    Assert.That(failure!.Message, Does.Contain("ends inside"));
  }

  [Test]
  [Category("Unit")]
  public void AFixedLaceThatDoesNotDivide_IsRefused() {
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [
        new MatroskaTestCluster {
          Blocks = [
            new MatroskaTestBlock { Frames = [_Payload(1, 10), _Payload(2, 11)], Lacing = TestLacing.Fixed },
          ],
        },
      ]);

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(document));
    Assert.That(failure!.Message, Does.Contain("equal length"));
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheSegmentSaysWhatItIsAndWhoWroteIt() {
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      title: "A recording",
      muxingApp: "Lavf63.1.100",
      writingApp: "somebody's editor");

    var metadata = VideoFormatRegistry.ReadMetadata(document);

    Assert.That(metadata.Title, Is.EqualTo("A recording"));
    // ffprobe reports the muxing application as the file's encoder, which is what EncodedBy means
    // here; the writing application is a different tool and is kept beside it rather than instead.
    Assert.That(metadata.EncodedBy, Is.EqualTo("Lavf63.1.100"));
    Assert.That(metadata.TextEntries.Any(t => t.Keyword == "Writing Application" && t.Text == "somebody's editor"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void TheCreationTimeCountsNanosecondsFrom2001() {
    // Verified against ffprobe, which reports a DateUTC of 139 651 750 000 000 000 as
    // 2005-06-05T08:09:10Z.
    var document = MatroskaTestContainer.Build([new MatroskaTestTrack()], dateUtc: 139_651_750_000_000_000L);

    Assert.That(VideoFormatRegistry.ReadMetadata(document).CreationTime,
      Is.EqualTo(new DateTimeOffset(2005, 6, 5, 8, 9, 10, TimeSpan.Zero)));
  }

  [Test]
  [Category("Unit")]
  public void TheDurationIsCountedInTheSegmentsOwnTicks() {
    // 500 against a millisecond tick is half a second, which is what ffprobe reports for the file
    // ffmpeg wrote for -t 0.5. Reading it as seconds would make it eight minutes.
    var document = MatroskaTestContainer.Build([new MatroskaTestTrack()], duration: 500.0);

    Assert.That(VideoFormatRegistry.ReadMetadata(document).Duration, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
  }

  [Test]
  [Category("Unit")]
  public void TagsAimedAtTheWholeFileAreItsMetadata() {
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      tags: [("TITLE", "A title in tags"), ("ARTIST", "Someone"), ("ALBUM", "A series"), ("COMMENT", "Measured")]);

    var metadata = VideoFormatRegistry.ReadMetadata(document);

    Assert.That(metadata.Title, Is.EqualTo("A title in tags"));
    Assert.That(metadata.Artist, Is.EqualTo("Someone"));
    Assert.That(metadata.Album, Is.EqualTo("A series"));
    Assert.That(metadata.TextEntries.Any(t => t.Keyword == "COMMENT" && t.Text == "Measured"), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ATagAimedAtATrackIsNotTheFilesMetadata() {
    // ffprobe reports the two separately — a per-track ENCODER against the stream and a global TITLE
    // against the format. Folding the per-track ones in would attribute a track's encoder to the film.
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack { Number = 1 }],
      tags: [("TITLE", "A title in tags")],
      trackTags: [(1UL, "ENCODER", "Lavc63.1.100 mjpeg")]);

    var metadata = VideoFormatRegistry.ReadMetadata(document);

    Assert.That(metadata.Title, Is.EqualTo("A title in tags"));
    Assert.That(metadata.TextEntries.Any(t => t.Keyword == "ENCODER"), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheInfoTitleOutranksATagSayingTheSameThing() {
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      title: "A title in Info",
      tags: [("TITLE", "A title in tags")]);

    Assert.That(VideoFormatRegistry.ReadMetadata(document).Title, Is.EqualTo("A title in Info"));
  }

  [Test]
  [Category("Unit")]
  public void AnAttachedPictureIsCoverArtAndNotAStream() {
    // A Matroska attachment has no track number, appears in no cluster and has no timestamp.
    // Counting it as a stream — which ffmpeg does as a convenience of its own model — would renumber
    // the real ones against what the file declares. The bytes cross in the format they were embedded
    // as, because that is what a muxer writing another container has to hand over.
    var picture = _Payload(3, 64);
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      attachments: [("cover.png", "image/png", "Front cover", picture), ("notes.txt", "text/plain", null, _Payload(4, 8))]);

    var metadata = VideoFormatRegistry.ReadMetadata(document);

    Assert.That(metadata.CoverArt, Has.Count.EqualTo(1));
    Assert.That(metadata.CoverArt[0].Data, Is.EqualTo(picture));
    Assert.That(metadata.CoverArt[0].MimeType, Is.EqualTo("image/png"));
    Assert.That(metadata.CoverArt[0].Description, Is.EqualTo("Front cover"));
    Assert.That(VideoFormatRegistry.ReadStreams(document), Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void AFileThatSaysNothingAboutItselfCarriesNoInventedMetadata() {
    var metadata = VideoFormatRegistry.ReadMetadata(MatroskaTestContainer.Build([new MatroskaTestTrack()]));

    Assert.That(metadata.Title, Is.Null);
    Assert.That(metadata.Artist, Is.Null);
    Assert.That(metadata.Album, Is.Null);
    Assert.That(metadata.EncodedBy, Is.Null);
    Assert.That(metadata.CreationTime, Is.Null);
    Assert.That(metadata.Duration, Is.Null);
    Assert.That(metadata.CoverArt, Is.Empty);
    Assert.That(metadata.TextEntries, Is.Empty);
  }

  // ------------------------------------------------------------------------------------------
  // The seam
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void AMotionJpegMatroskaReachesTheSameDecoderAnAviDoes() {
    // The point of the demux/decode split, tested at the seam rather than in either half. This
    // container has never heard of JPEG and that decoder has never heard of EBML; what joins them is
    // a MediaStreamInfo naming V_MJPEG, which the codec collects as another of its spellings.
    var frames = new[] { _Jpeg(0), _Jpeg(1), _Jpeg(2) };
    var document = MatroskaTestContainer.Build("V_MJPEG", frames);

    var pictures = VideoFormatRegistry.DecodeFrames(document).ToList();

    Assert.That(pictures, Has.Count.EqualTo(3));
    for (var i = 0; i < frames.Length; ++i) {
      var alone = JpegFile.ToRawImage(JpegReader.FromSpan(frames[i]));
      Assert.That(pictures[i].Image.Width, Is.EqualTo(_WIDTH));
      Assert.That(pictures[i].Image.Height, Is.EqualTo(_HEIGHT));
      Assert.That(pictures[i].Image.ToRgb24(), Is.EqualTo(alone.ToRgb24()));
    }
  }

  [Test]
  [Category("Unit")]
  public void AStreamNothingDecodes_StillComesApartIntoItsPackets() {
    // A WebM full of AV1 is a perfectly good WebM, and copying its packets into another container
    // needs no decoder at all. The refusal happens when a decoder is asked for, and names the codec
    // the file names rather than a four-character code no file contains.
    var document = MatroskaTestContainer.Build("V_AV1", [_Payload(1, 20), _Payload(2, 30)]);

    Assert.That(_Packets(document).Select(p => p.Data.Length), Is.EqualTo(new[] { 20, 30 }));

    var stream = VideoFormatRegistry.ReadStreams(document)[0];
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.False);

    var failure = Assert.Throws<NotSupportedException>(() => VideoFormatRegistry.CreateDecoder(stream));
    Assert.That(failure!.Message, Does.Contain("V_AV1"));
  }

  [Test]
  [Category("Unit")]
  public void AVideoStreamWithNoCodeAtAll_IsNotMistakenForAnUncompressedOne() {
    // Zero means two different things depending on who is speaking. A container that names codecs
    // with a code and states zero is stating BI_RGB; one that names them with text states no code at
    // all, and every Matroska track would otherwise arrive at the uncompressed decoder — VP9 and
    // Vorbis included — and be refused for holding the wrong number of bytes rather than for being a
    // codec nothing here reads.
    // The track has to be one nothing here decodes, or the test would pass for the wrong reason.
    var stream = _Streams(MatroskaTestContainer.Build([new MatroskaTestTrack { CodecId = "V_THEORA" }]))[0];

    Assert.That(stream.Codec, Is.EqualTo(CodecTag.None));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AVideoForWindowsTrackOfBiRgb_ReachesTheUncompressedDecoder() {
    // The one CodecID whose zero does mean BI_RGB, because the BITMAPINFOHEADER it carries is where
    // the zero came from. The packets are the pixel array of a Windows bitmap, rows padded to four
    // bytes, exactly as an uncompressed AVI's are.
    var header = MatroskaTestContainer.BitmapInfoHeader("\0\0\0\0", _WIDTH, -_HEIGHT, 24);
    var pixels = _Payload(7, ((_WIDTH * 3) + 3) / 4 * 4 * _HEIGHT);
    var document = MatroskaTestContainer.Build(
      [new MatroskaTestTrack { CodecId = "V_MS/VFW/FOURCC", CodecPrivate = header }],
      [new MatroskaTestCluster { Blocks = [_Block(0, pixels)] }]);

    var pictures = VideoFormatRegistry.DecodeFrames(document).ToList();

    Assert.That(pictures, Has.Count.EqualTo(1));
    Assert.That(pictures[0].Image.Width, Is.EqualTo(_WIDTH));
    Assert.That(pictures[0].Image.Height, Is.EqualTo(_HEIGHT));
  }

  // ------------------------------------------------------------------------------------------
  // Helpers
  // ------------------------------------------------------------------------------------------

  private static byte[] _Simple()
    => MatroskaTestContainer.Build(
      [new MatroskaTestTrack()],
      [new MatroskaTestCluster { Blocks = [_Block(0, _Payload(1, 6))] }]);

  private static MatroskaTestBlock _Block(short relative, byte[] frame)
    => new() { Relative = relative, Frames = [frame] };

  private static IReadOnlyList<CodedPacket> _Packets(byte[] document)
    => MatroskaContainer.ReadPackets(MatroskaReader.FromBytes(document)).ToList();

  private static IReadOnlyList<MediaStreamInfo> _Streams(byte[] document)
    => MatroskaContainer.Streams(MatroskaReader.FromBytes(document));

  /// <summary>Bytes no two of which are alike, so a packet taken from the wrong place is visible.</summary>
  private static byte[] _Payload(int seed, int length) {
    var result = new byte[length];
    for (var i = 0; i < length; ++i)
      result[i] = (byte)((seed * 37) + (i * 11) + 1);

    return result;
  }

  /// <summary>A JPEG whose picture depends on the seed, so that two frames never look alike.</summary>
  private static byte[] _Jpeg(int seed) {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var i = 0; i < _WIDTH * _HEIGHT; ++i) {
      pixels[i * 3] = (byte)(((i * 7) + (seed * 61)) & 0xFF);
      pixels[(i * 3) + 1] = (byte)(((i * 3) + (seed * 29)) & 0xFF);
      pixels[(i * 3) + 2] = (byte)(((i * 11) + (seed * 97)) & 0xFF);
    }

    var raw = new RawImage { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };
    return JpegWriter.ToBytes(JpegFile.FromRawImage(raw));
  }
}
