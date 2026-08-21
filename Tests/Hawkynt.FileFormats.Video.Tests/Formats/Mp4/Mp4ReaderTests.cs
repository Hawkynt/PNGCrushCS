using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FileFormat.Core;
using FileFormat.Jpeg;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Mp4.Tests;

/// <summary>
/// The ISO base media reader's behaviour — MP4, QuickTime MOV, M4V and 3GP being one format under
/// four names.
/// </summary>
/// <remarks>
/// Everything asserted here was first measured against ffmpeg and ffprobe on files ffmpeg itself
/// wrote, and the numbers in the assertions are the numbers ffprobe reported. Where a form is one
/// ffmpeg will not produce for a file small enough to check by hand — <c>co64</c>, <c>stz2</c>, a
/// 64-bit box size — the real file was made by rewriting ffmpeg's own output into that form and
/// checking that ffprobe still read the same packets out of it, and only then written as a built
/// container here.
/// <para/>
/// The reader is a demuxer and nothing else, so most of what is tested is packets: how many, how
/// long, and when each is due. The one test that decodes exists to prove the seam rather than the
/// codec — a MOV of Motion JPEG reaches the same decoder an AVI of it does, and neither container
/// knows anything about JPEG.
/// </remarks>
[TestFixture]
public sealed class Mp4ReaderTests {

  private const int _WIDTH = Mp4TestContainer.FRAME_WIDTH;
  private const int _HEIGHT = Mp4TestContainer.FRAME_HEIGHT;

  // ------------------------------------------------------------------------------------------
  // Opening
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Mp4Reader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromSpan_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => Mp4Reader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void WithoutAMovieBox_IsRefused() {
    // An ftyp and an mdat and nothing else. The bytes are there but nothing says where one packet
    // stops and the next begins, which for this format lives entirely in moov.
    var headless = Mp4TestContainer.Build([new Mp4TestTrack { Samples = [[1, 2, 3]] }]);
    var type = _Find(headless, "moov") - 4;
    headless[type] = (byte)'f';
    headless[type + 1] = (byte)'r';
    headless[type + 2] = (byte)'e';
    headless[type + 3] = (byte)'e';

    Assert.Throws<InvalidDataException>(() => Mp4Reader.FromBytes(headless));
  }

  [Test]
  [Category("Unit")]
  public void AFragmentedFile_IsRefusedByName() {
    // A fragmented file keeps its sample tables in its moof boxes, so the ones in moov are empty and
    // walking them reports a film of no packets. That is indistinguishable from a container that
    // really holds none, which is why this refuses instead of returning nothing.
    var fragmented = Mp4TestContainer.Build([new Mp4TestTrack { Samples = [[1, 2, 3]] }], fragment: true);

    var failure = Assert.Throws<NotSupportedException>(() => Mp4Reader.FromBytes(fragmented));
    Assert.That(failure!.Message, Does.Contain("moof"));
  }

  [Test]
  [Category("Unit")]
  public void TheSignatureIsReadAtOffsetFour() {
    // There is no fixed byte at offset zero: a file begins with a box length, which is four bytes of
    // anything at all. The type after it is what says what the file is.
    var file = Mp4TestContainer.Build([new Mp4TestTrack { Samples = [[1, 2, 3]] }]);

    Assert.That(Mp4Container.MatchesSignature(file), Is.True);
    Assert.That(VideoFormatRegistry.Detect(file), Is.EqualTo(VideoFormat.Mp4));
  }

  [Test]
  [Category("Unit")]
  public void AQuickTimeFileWithoutAFileTypeBox_IsStillRecognised() {
    // Written before ftyp existed, so it begins straight into one of the boxes only this format has.
    var classic = new byte[] { 0, 0, 0, 8, (byte)'w', (byte)'i', (byte)'d', (byte)'e' };

    Assert.That(Mp4Container.MatchesSignature(classic), Is.True);
  }

  // ------------------------------------------------------------------------------------------
  // Packets
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void EverySampleOfTheTableIsAPacket() {
    var samples = new[] { _Payload(1, 10), _Payload(2, 20), _Payload(3, 30) };

    var packets = _Packets(Mp4TestContainer.Build("jpeg", _WIDTH, _HEIGHT, samples));

    Assert.That(packets, Has.Count.EqualTo(3));
    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 10, 20, 30 }));
  }

  [Test]
  [Category("Unit")]
  public void EachPacketHoldsExactlyTheBytesTheTableNames() {
    var samples = new[] { _Payload(1, 10), _Payload(2, 20), _Payload(3, 30) };

    var packets = _Packets(Mp4TestContainer.Build("jpeg", _WIDTH, _HEIGHT, samples));

    for (var i = 0; i < samples.Length; ++i)
      Assert.That(packets[i].Data.ToArray(), Is.EqualTo(samples[i]), $"packet {i}");
  }

  [Test]
  [Category("Unit")]
  public void PacketsCarryTheirPositionInTheTracksOwnTimeScale() {
    // Measured against ffprobe on an ffmpeg-written MP4 of five frames at ten a second: a media time
    // scale of 10240 and a per-sample duration of 1024, so the timestamps run 0, 1024, 2048 and the
    // stream's time base is 1/10240.
    var track = new Mp4TestTrack {
      Samples = [_Payload(1, 8), _Payload(2, 8), _Payload(3, 8)],
      Timescale = 10240,
      SampleDuration = 1024,
    };

    var container = Mp4Reader.FromBytes(Mp4TestContainer.Build([track]));
    var packets = Mp4Container.ReadPackets(container).ToList();

    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 1024, 2048 }));
    Assert.That(packets.Select(p => p.DecodeTimestamp), Is.EqualTo(new long?[] { 0, 1024, 2048 }));
    Assert.That(packets.Select(p => p.Duration), Is.EqualTo(new long?[] { 1024, 1024, 1024 }));

    var stream = Mp4Container.Streams(container)[0];
    Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 10240)));
    Assert.That(stream.TimeBase.Scale(packets[2].PresentationTimestamp!.Value), Is.EqualTo(TimeSpan.FromSeconds(0.2)));
  }

  [Test]
  [Category("Unit")]
  public void SeveralSamplesToAChunk_AreWalkedOffTheChunksStart() {
    // Nothing states a sample's offset. It is its chunk's offset plus the lengths of the samples
    // before it in that chunk, which is the one computation the whole format turns on — a reader
    // that took every sample from the start of its chunk would return the same bytes several times.
    var samples = new[] { _Payload(1, 5), _Payload(2, 7), _Payload(3, 11), _Payload(4, 13), _Payload(5, 3) };
    var track = new Mp4TestTrack { Samples = samples, SamplesPerChunk = 2 };

    var packets = _Packets(Mp4TestContainer.Build([track]));

    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 5, 7, 11, 13, 3 }));
    for (var i = 0; i < samples.Length; ++i)
      Assert.That(packets[i].Data.ToArray(), Is.EqualTo(samples[i]), $"packet {i}");
  }

  [Test]
  [Category("Unit")]
  public void AChunkTableOfSeveralRuns_ChangesTheGroupingWhereItSaysTo() {
    // The shape ffmpeg writes for a track whose chunks are not all alike: the AAC track of an MP4 it
    // muxed came out with five stsc entries. A reader that took the first run for the whole table
    // would read every packet after the first change from the wrong offset.
    var samples = new[] {
      _Payload(1, 3), _Payload(2, 4), _Payload(3, 5), _Payload(4, 6),
      _Payload(5, 7), _Payload(6, 8), _Payload(7, 9),
    };
    // Chunk 1 holds one sample, chunks 2 and 3 hold two each, chunk 4 onwards holds two — seven
    // samples in four chunks of 1, 2, 2, 2.
    var track = new Mp4TestTrack { Samples = samples, ChunkRuns = [(1, 1), (2, 2)] };

    var packets = _Packets(Mp4TestContainer.Build([track]));

    Assert.That(packets, Has.Count.EqualTo(samples.Length));
    for (var i = 0; i < samples.Length; ++i)
      Assert.That(packets[i].Data.ToArray(), Is.EqualTo(samples[i]), $"packet {i}");
  }

  [Test]
  [Category("Unit")]
  public void ABoxDeclaringNoLengthRunsToTheEndOfTheFile() {
    // A stated length of zero means "to the end", which is what a writer emits for a box it is still
    // filling. The box patched here is the movie box rather than the media one on purpose: mdat is
    // never read, so a reader that mishandled a zero length on it would pass anyway. Getting this
    // wrong on a box that is read costs the whole file — a length of zero taken literally is shorter
    // than the header, and the walk stops there.
    var samples = new[] { _Payload(1, 6), _Payload(2, 9) };
    var container = Mp4TestContainer.Build([new Mp4TestTrack { Samples = samples }]);
    var size = _Find(container, "moov") - 8;
    container[size] = container[size + 1] = container[size + 2] = container[size + 3] = 0;

    Assert.That(_Packets(container).Select(p => p.Data.ToArray()), Is.EqualTo(samples));
  }

  [Test]
  [Category("Unit")]
  public void SixtyFourBitChunkOffsets_AreReadTheSameAsThirtyTwoBitOnes() {
    // co64 rather than stco, which no writer produces below four gigabytes. The form was checked on a
    // real file first: ffmpeg's own MP4 rewritten from stco to co64 is still read by ffprobe as the
    // same five packets at the same offsets.
    var samples = new[] { _Payload(1, 6), _Payload(2, 9) };

    var wide = _Packets(Mp4TestContainer.Build([new Mp4TestTrack { Samples = samples, ChunkOffsets64 = true }]));
    var narrow = _Packets(Mp4TestContainer.Build([new Mp4TestTrack { Samples = samples }]));

    Assert.That(wide.Select(p => p.Data.ToArray()), Is.EqualTo(narrow.Select(p => p.Data.ToArray())));
  }

  [TestCase(4)]
  [TestCase(8)]
  [TestCase(16)]
  [Category("Unit")]
  public void CompactSampleSizes_AreReadAtEveryFieldWidth(int bits) {
    // stz2 packs the sizes into fields narrower than a word, four of them sharing a byte at the
    // narrowest. Also checked on a real file, by rewriting ffmpeg's stsz into a sixteen-bit stz2 and
    // confirming ffprobe read the same five packet sizes back out.
    var samples = new[] { _Payload(1, 3), _Payload(2, 5), _Payload(3, 7) };
    var track = new Mp4TestTrack { Samples = samples, CompactSizeBits = bits };

    var packets = _Packets(Mp4TestContainer.Build([track]));

    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 3, 5, 7 }));
    for (var i = 0; i < samples.Length; ++i)
      Assert.That(packets[i].Data.ToArray(), Is.EqualTo(samples[i]), $"packet {i}");
  }

  [Test]
  [Category("Unit")]
  public void OneSizeStatedForEverySample_MeansNoSizeTableAtAll() {
    var samples = new[] { _Payload(1, 4), _Payload(2, 4), _Payload(3, 4) };
    var track = new Mp4TestTrack { Samples = samples, FixedSampleSize = true };

    var packets = _Packets(Mp4TestContainer.Build([track]));

    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 4, 4, 4 }));
    Assert.That(packets[2].Data.ToArray(), Is.EqualTo(samples[2]));
  }

  [Test]
  [Category("Unit")]
  public void ASixtyFourBitBoxSize_IsWalkedLikeAThirtyTwoBitOne() {
    // A size of 1 means the real size follows the type as eight bytes. Checked on a real file too:
    // ffmpeg's MP4 with a widened moov is read by ffprobe as the same five packets.
    var samples = new[] { _Payload(1, 6), _Payload(2, 9) };

    var packets = _Packets(Mp4TestContainer.Build([new Mp4TestTrack { Samples = samples }], wideMovieBox: true));

    Assert.That(packets.Select(p => p.Data.ToArray()), Is.EqualTo(samples));
  }

  [Test]
  [Category("Unit")]
  public void AMovieBoxWrittenBeforeTheMedia_ReadsTheSameAsOneWrittenAfterIt() {
    // A writer producing a file in one pass cannot know its own sample tables until it has written
    // the samples, so moov ends up last — which is what both reference files are. A file prepared for
    // streaming has it first. Same packets either way.
    var samples = new[] { _Payload(1, 6), _Payload(2, 9), _Payload(3, 12) };

    var trailing = _Packets(Mp4TestContainer.Build([new Mp4TestTrack { Samples = samples }]));
    var leading = _Packets(Mp4TestContainer.Build([new Mp4TestTrack { Samples = samples }], movieFirst: true));

    Assert.That(leading.Select(p => p.Data.ToArray()), Is.EqualTo(samples));
    Assert.That(leading.Select(p => p.Data.ToArray()), Is.EqualTo(trailing.Select(p => p.Data.ToArray())));
  }

  [Test]
  [Category("Unit")]
  public void ASampleStatedOutsideTheFile_IsRefusedRatherThanPaddedOut() {
    // Half a packet is not a packet. Handing back what is there, zero-filled to the stated length,
    // would present bytes nothing wrote as a packet that was read.
    var container = Mp4TestContainer.Build([new Mp4TestTrack { Samples = [_Payload(1, 8), _Payload(2, 8)] }]);
    // Past the version-and-flags word, the fixed size and the count, to the first sample's entry.
    var entry = _Find(container, "stsz") + 4 + 4 + 4;
    container[entry + 2] = 0xFF;
    container[entry + 3] = 0xFF;

    Assert.Throws<InvalidDataException>(() => _Packets(container));
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreWindowsOntoTheFileRatherThanCopies() {
    var samples = new[] { _Payload(1, 12), _Payload(2, 12) };
    var bytes = Mp4TestContainer.Build([new Mp4TestTrack { Samples = samples }]);
    var container = Mp4Reader.FromBytes(bytes);

    // The proof that nothing was copied: every packet is a slice of the very array the container was
    // opened over. A demuxer that kept its own copy of a film would double it.
    foreach (var packet in Mp4Container.ReadPackets(container)) {
      Assert.That(MemoryMarshal.TryGetArray(packet.Data, out var segment), Is.True);
      Assert.That(segment.Array, Is.SameAs(bytes));
    }
  }

  [Test]
  [Category("Unit")]
  public void TheWalkIsLazyAndCanBeRunMoreThanOnce() {
    var samples = new[] { _Payload(1, 6), _Payload(2, 6), _Payload(3, 6) };
    var container = Mp4Reader.FromBytes(Mp4TestContainer.Build([new Mp4TestTrack { Samples = samples }]));

    Assert.That(Mp4Container.ReadPackets(container).First().Data.ToArray(), Is.EqualTo(samples[0]));
    Assert.That(Mp4Container.ReadPackets(container).Count(), Is.EqualTo(3));
    Assert.That(Mp4Container.ReadPackets(container).Skip(1).First().Data.ToArray(), Is.EqualTo(samples[1]));
  }

  // ------------------------------------------------------------------------------------------
  // Timing
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void WithoutASyncTable_EverySampleMayBeDecodedFrom() {
    // Which is what an all-intra codec produces, and what ffprobe reports for the Motion JPEG MOV:
    // a keyframe flag on all five of its packets.
    var packets = _Packets(Mp4TestContainer.Build([new Mp4TestTrack { Samples = [_Payload(1, 4), _Payload(2, 4), _Payload(3, 4)] }]));

    Assert.That(packets.Select(p => p.IsKeyFrame), Is.All.True);
  }

  [Test]
  [Category("Unit")]
  public void ASyncTable_NamesTheSamplesDecodingMayBeginAt() {
    // Measured against ffprobe on the ffmpeg-written MPEG-4 MP4, whose stss names sample 1 alone and
    // whose flags read K, then four packets with none.
    var track = new Mp4TestTrack {
      Samples = [_Payload(1, 4), _Payload(2, 4), _Payload(3, 4), _Payload(4, 4)],
      SyncSamples = [1, 3],
    };

    var packets = _Packets(Mp4TestContainer.Build([track]));

    Assert.That(packets.Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, false, true, false }));
  }

  [Test]
  [Category("Unit")]
  public void ACompositionOffsetTable_MovesDisplayAheadOfDecoding() {
    // The one thing a decode timestamp is for. A codec that predicts in both directions decodes a
    // frame before the frames it is displayed after, and ctts is where that difference is written.
    var track = new Mp4TestTrack {
      Samples = [_Payload(1, 4), _Payload(2, 4), _Payload(3, 4)],
      SampleDuration = 10,
      CompositionOffsets = [0, 20, -10],
    };

    var packets = _Packets(Mp4TestContainer.Build([track]));

    Assert.That(packets.Select(p => p.DecodeTimestamp), Is.EqualTo(new long?[] { 0, 10, 20 }));
    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 30, 10 }));
  }

  [Test]
  [Category("Unit")]
  public void AnEditListMovesEveryTimestampBackByItsMediaTime() {
    // Measured: ffmpeg writes an elst of media time 1024 for the AAC track of an MP4 it muxes — the
    // encoder's priming samples — and ffprobe reports that track's first packet at -1024 rather than
    // at zero. A reader ignoring the box would disagree with every other tool about where the file
    // starts.
    var track = new Mp4TestTrack {
      Samples = [_Payload(1, 4), _Payload(2, 4), _Payload(3, 4)],
      Timescale = 44100,
      SampleDuration = 1024,
      Edits = [(500, 1024)],
    };

    var packets = _Packets(Mp4TestContainer.Build([track]));

    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { -1024, 0, 1024 }));
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyEditDelaysTheTrackByItsOwnDuration() {
    // A media time of -1 is a gap before the track begins, stated in the movie's time scale rather
    // than the track's — so the two clocks have to be converted between or the delay is wrong by
    // whatever ratio separates them. The movie runs at 1000 units a second and this track at 100, so
    // 200 movie units are 20 of the track's.
    var track = new Mp4TestTrack {
      Samples = [_Payload(1, 4), _Payload(2, 4)],
      Timescale = 100,
      SampleDuration = 10,
      Edits = [(200, -1), (300, 0)],
    };

    var packets = _Packets(Mp4TestContainer.Build([track]));

    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 20, 30 }));
  }

  // ------------------------------------------------------------------------------------------
  // Streams
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void ATrackIsDescribedFromItsHeadersWithoutAPacketBeingRead() {
    var track = new Mp4TestTrack {
      Codec = "mp4v",
      Width = 61,
      Height = 37,
      Depth = 24,
      Timescale = 10240,
      SampleDuration = 1024,
      Samples = [_Payload(1, 6), _Payload(2, 6), _Payload(3, 6)],
    };

    var stream = Mp4Container.Streams(Mp4Reader.FromBytes(Mp4TestContainer.Build([track])))[0];

    Assert.That(stream.Index, Is.Zero);
    Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(stream.Codec.ToString(), Is.EqualTo("mp4v"));
    Assert.That(stream.Width, Is.EqualTo(61));
    Assert.That(stream.Height, Is.EqualTo(37));
    Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
    Assert.That(stream.DeclaredFrameCount, Is.EqualTo(3));
    // One duration stated for every sample is the frame rate the other way up: 10240 over 1024.
    Assert.That(stream.FrameRate, Is.EqualTo(new Rational(10240, 1024)));
  }

  [Test]
  [Category("Unit")]
  public void TheSampleEntryGoesAcrossWholeAsCodecPrivateData() {
    // Verbatim, header included. What describes the codec is inside it as boxes of its own — 'esds'
    // for MPEG-4, 'avcC' for H.264 — and picking the right one out would mean this container knowing
    // the codecs, which is what the split exists to prevent.
    var bytes = Mp4TestContainer.Build([new Mp4TestTrack { Codec = "mp4v", Samples = [_Payload(1, 4)] }]);
    var stream = Mp4Container.Streams(Mp4Reader.FromBytes(bytes))[0];

    var entry = stream.CodecPrivateData.Span;
    Assert.That(entry.Length, Is.GreaterThan(8));
    Assert.That(Encoding.ASCII.GetString(entry.Slice(4, 4)), Is.EqualTo("mp4v"));
    Assert.That((entry[0] << 24) | (entry[1] << 16) | (entry[2] << 8) | entry[3], Is.EqualTo(entry.Length));
  }

  [Test]
  [Category("Unit")]
  public void EveryTrackIsReportedAndKeepsItsPosition() {
    // Sound as well as pictures, because a track's number is its position among all of them: leaving
    // the ones nothing decodes out would make the rest go looking under the wrong index, and a remux
    // has to carry the sound across whether or not anything here can decode it.
    var video = new Mp4TestTrack { Codec = "mp4v", Samples = [_Payload(1, 4), _Payload(2, 4)] };
    var audio = new Mp4TestTrack { Handler = "soun", Codec = "mp4a", Timescale = 44100, Samples = [_Payload(3, 4)] };

    var streams = Mp4Container.Streams(Mp4Reader.FromBytes(Mp4TestContainer.Build([video, audio])));

    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(streams[1].Codec.ToString(), Is.EqualTo("mp4a"));
    Assert.That(streams[1].TimeBase, Is.EqualTo(new Rational(1, 44100)));
  }

  [Test]
  [Category("Unit")]
  public void PacketsOfSeveralTracksComeOutInTheOrderTheFileStoresThem() {
    // Storage order and not track order. ffprobe reports an ffmpeg-muxed MP4 with sound as its two
    // tracks interleaved, and a reader handing back one whole track and then the other would be
    // giving the packets in an order nothing could play.
    var video = new Mp4TestTrack { Codec = "mp4v", Samples = [_Payload(1, 4), _Payload(2, 4), _Payload(3, 4)] };
    var audio = new Mp4TestTrack { Handler = "soun", Codec = "mp4a", Timescale = 44100, Samples = [_Payload(4, 5), _Payload(5, 5), _Payload(6, 5)] };

    var packets = _Packets(Mp4TestContainer.Build([video, audio]));

    Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 0, 1, 0, 1, 0, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void OneTracksPacketsCanBeWalkedWithoutTheOthers() {
    var video = new Mp4TestTrack { Codec = "mp4v", Samples = [_Payload(1, 4), _Payload(2, 4)] };
    var audio = new Mp4TestTrack { Handler = "soun", Codec = "mp4a", Samples = [_Payload(3, 5), _Payload(4, 5), _Payload(5, 5)] };

    var container = Mp4Reader.FromBytes(Mp4TestContainer.Build([video, audio]));

    Assert.That(Mp4Container.ReadPackets(container, 0).Select(p => p.Data.Length), Is.EqualTo(new[] { 4, 4 }));
    Assert.That(Mp4Container.ReadPackets(container, 1).Select(p => p.Data.Length), Is.EqualTo(new[] { 5, 5, 5 }));
    Assert.That(Mp4Container.ReadPackets(container, 7), Is.Empty);
  }

  [TestCase("hev1")]
  [TestCase("mp4v")]
  [Category("Unit")]
  public void UnsupportedCodec_StillDemuxes(string code) {
    // The refusal is the codec's and not the container's. A file nothing here decodes still comes
    // apart into its packets, which is what a remux into another container would move.
    var container = Mp4Reader.FromBytes(Mp4TestContainer.Build(code, 64, 48, [_Payload(1, 16), _Payload(2, 16)]));

    var streams = Mp4Container.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Codec.ToString(), Is.EqualTo(code));
    Assert.That(Mp4Container.ReadPackets(container).Count(), Is.EqualTo(2));
    Assert.That(VideoFormatRegistry.CanDecode(streams[0]), Is.False);
  }

  [TestCase("hev1")]
  [Category("Unit")]
  public void UnsupportedCodec_IsRefusedWithItsCodeWhenAPictureIsAskedFor(string code) {
    var container = Mp4TestContainer.Build(code, 64, 48, [_Payload(1, 16)]);

    var failure = Assert.Throws<NotSupportedException>(() => VideoFormatRegistry.DecodeFrames(container).ToList());
    Assert.That(failure!.Message, Does.Contain(code));
  }

  // ------------------------------------------------------------------------------------------
  // What the split between demuxing and decoding buys
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void MotionJpeg_ReachesTheSameDecoderAnAviReaches() {
    // The end-to-end proof that the seam works across a second container. ffmpeg's MOV muxer writes
    // 'jpeg' into the sample entry where its AVI muxer writes 'MJPG', for packets that are byte for
    // byte the same JPEGs — so the codec collects both spellings and neither container knows what the
    // other calls it. Measured on ffmpeg's own MOV: five packets, five frames, each identical to the
    // PNG ffmpeg extracted from the same file.
    var jpegs = new[] { _Jpeg(0), _Jpeg(1), _Jpeg(2) };
    var frames = VideoFormatRegistry.DecodeFrames(Mp4TestContainer.Build("jpeg", _WIDTH, _HEIGHT, jpegs))
      .Select(f => f.Image)
      .ToList();

    Assert.That(frames, Has.Count.EqualTo(3));
    for (var i = 0; i < jpegs.Length; ++i) {
      var direct = JpegFile.ToRawImage(JpegReader.FromBytes(jpegs[i]));
      Assert.That(frames[i].Width, Is.EqualTo(direct.Width), $"frame {i} width");
      Assert.That(frames[i].ToRgb24(), Is.EqualTo(direct.ToRgb24()), $"frame {i} pixels");
    }
  }

  [Test]
  [Category("Unit")]
  public void MotionJpeg_FramesComeOutInTheOrderTheyWereWritten() {
    var frames = VideoFormatRegistry.DecodeFrames(Mp4TestContainer.Build("jpeg", _WIDTH, _HEIGHT, [_Jpeg(0), _Jpeg(1), _Jpeg(2)]))
      .Select(f => f.Image)
      .ToList();

    Assert.That(frames[0].ToRgb24(), Is.Not.EqualTo(frames[1].ToRgb24()));
    Assert.That(frames[1].ToRgb24(), Is.Not.EqualTo(frames[2].ToRgb24()));
  }

  [Test]
  [Category("Unit")]
  public void OnlyTheFramesAskedFor_AreDecoded() {
    // The last packet is not a JPEG at all and throws when decoded. Taking the first frame must still
    // succeed: a walk that decoded eagerly would let the broken one at the end take the good one at
    // the front with it.
    var container = Mp4TestContainer.Build("jpeg", _WIDTH, _HEIGHT, [_Jpeg(0), _Payload(9, 16)]);

    Assert.That(VideoFormatRegistry.DecodeFrames(container).First().Image.Width, Is.EqualTo(_WIDTH));
    Assert.That(() => VideoFormatRegistry.DecodeFrames(container).ToList(), Throws.Exception);
  }

  [Test]
  [Category("Unit")]
  public void ASoundOnlyFileDemuxesAndIsRefusedOnlyWhenPicturesAreAskedFor() {
    var audio = new Mp4TestTrack { Handler = "soun", Codec = "mp4a", Samples = [_Payload(1, 8), _Payload(2, 8)] };
    var bytes = Mp4TestContainer.Build([audio]);

    Assert.That(Mp4Container.ReadPackets(Mp4Reader.FromBytes(bytes)).Count(), Is.EqualTo(2));
    Assert.Throws<InvalidDataException>(() => VideoFormatRegistry.DecodeFrames(bytes).ToList());
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheCreationTimeIsCountedFromNineteenOhFour() {
    // MP4's epoch is 1904-01-01 UTC, which is neither the Unix one nor anything the framework knows.
    // The number below is what ffmpeg wrote into mvhd for -metadata creation_time=2001-02-03T04:05:06Z,
    // read off the file: 0xB6A133F2.
    var expected = new DateTimeOffset(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);
    var seconds = expected.ToUnixTimeSeconds() + Mp4TestContainer.EPOCH_OFFSET;
    Assert.That(seconds, Is.EqualTo(0xB6A133F2), "the sample's own mvhd field");

    var bytes = Mp4TestContainer.Build([new Mp4TestTrack { Samples = [_Payload(1, 4)] }], creationTime: seconds);

    Assert.That(VideoFormatRegistry.ReadMetadata(bytes).CreationTime, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void ACreationTimeOfZeroMeansTheWriterSaidNothing() {
    // Rather than that the file was made in 1904. ffmpeg writes zero unless a creation time was
    // given, and ffprobe reports no creation time at all for such a file.
    var bytes = Mp4TestContainer.Build([new Mp4TestTrack { Samples = [_Payload(1, 4)] }]);

    Assert.That(VideoFormatRegistry.ReadMetadata(bytes).CreationTime, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void TheDurationIsWhatTheMovieHeaderClaims() {
    // 500 units of a 1000-unit-a-second clock, which is the half second ffmpeg wrote for -t 0.5.
    var bytes = Mp4TestContainer.Build([new Mp4TestTrack { Samples = [_Payload(1, 4)] }], movieDuration: 500);

    Assert.That(VideoFormatRegistry.ReadMetadata(bytes).Duration, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
  }

  [Test]
  [Category("Unit")]
  public void TheItemListIsReadIntoTheMetadata() {
    var bytes = Mp4TestContainer.Build(
      [new Mp4TestTrack { Samples = [_Payload(1, 4)] }],
      tags: [("©nam", "A title"), ("©ART", "An author"), ("©alb", "An album"), ("©too", "A tool"), ("©cmt", "A remark")]);

    var metadata = VideoFormatRegistry.ReadMetadata(bytes);

    Assert.That(metadata.Title, Is.EqualTo("A title"));
    Assert.That(metadata.Artist, Is.EqualTo("An author"));
    Assert.That(metadata.Album, Is.EqualTo("An album"));
    Assert.That(metadata.EncodedBy, Is.EqualTo("A tool"));
    Assert.That(metadata.TextEntries.Any(t => t.Keyword == "Comment" && t.Text == "A remark"), Is.True);
    Assert.That(metadata.IsEmpty, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AQuickTimeTextAtomCarriesItsOwnLength() {
    // The other of the two shapes a tag is written in, and the one ffmpeg's MOV muxer uses: the atom
    // sits straight in udta with a sixteen-bit length and a language in front of the text, where an
    // iTunes-style one wraps it in meta, ilst and a data box. Both were measured coming out of the
    // same version of ffmpeg, one per container.
    var bytes = Mp4TestContainer.Build(
      [new Mp4TestTrack { Samples = [_Payload(1, 4)] }],
      quickTimeTags: [("©swr", "Lavf63.1.100")]);

    Assert.That(VideoFormatRegistry.ReadMetadata(bytes).EncodedBy, Is.EqualTo("Lavf63.1.100"));
  }

  [Test]
  [Category("Unit")]
  public void CoverArtIsKeptInTheFormatItWasEmbeddedAs() {
    // Not decoded. That is what a muxer writing another container has to hand over, and decoding it
    // first could only lose the original.
    var png = _Jpeg(0);
    png[0] = 0x89;
    png[1] = (byte)'P';
    png[2] = (byte)'N';
    png[3] = (byte)'G';

    var bytes = Mp4TestContainer.Build([new Mp4TestTrack { Samples = [_Payload(1, 4)] }], cover: png);
    var metadata = VideoFormatRegistry.ReadMetadata(bytes);

    Assert.That(metadata.CoverArt, Has.Count.EqualTo(1));
    Assert.That(metadata.CoverArt[0].Data, Is.EqualTo(png));
    Assert.That(metadata.CoverArt[0].MimeType, Is.EqualTo("image/png"));
  }

  [Test]
  [Category("Unit")]
  public void ATrackCarriesItsNameAndItsLanguage() {
    // 0x10B5 is what ffmpeg wrote for -metadata:s:v:0 language=deu: three ISO 639-2 letters packed
    // five bits each, each stored as its distance from 0x60.
    var track = new Mp4TestTrack {
      Samples = [_Payload(1, 4)],
      Language = 0x10B5,
      Name = "Main camera",
    };

    var metadata = VideoFormatRegistry.ReadMetadata(Mp4TestContainer.Build([track]));

    Assert.That(metadata.Streams, Has.Count.EqualTo(1));
    Assert.That(metadata.Streams[0].Language, Is.EqualTo("deu"));
    Assert.That(metadata.Streams[0].Name, Is.EqualTo("Main camera"));
  }

  [TestCase(0)]
  [TestCase(0x7FFF)]
  [Category("Unit")]
  public void ALanguageFieldThatIsNotThreeLetters_IsLeftUnstated(int packed) {
    // Zero, and QuickTime's 0x7FFF for "unspecified" — which ffmpeg writes into every MOV it muxes,
    // and which unpacked as letters would give three characters of nonsense.
    var bytes = Mp4TestContainer.Build([new Mp4TestTrack { Samples = [_Payload(1, 4)], Language = packed }]);

    Assert.That(VideoFormatRegistry.ReadMetadata(bytes).Streams[0].Language, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void AFileThatSaysNothingAboutItselfCarriesNoInventedMetadata() {
    var metadata = VideoFormatRegistry.ReadMetadata(Mp4TestContainer.Build([new Mp4TestTrack { Samples = [_Payload(1, 4)] }]));

    Assert.That(metadata.Title, Is.Null);
    Assert.That(metadata.Artist, Is.Null);
    Assert.That(metadata.Album, Is.Null);
    Assert.That(metadata.CreationTime, Is.Null);
    Assert.That(metadata.CoverArt, Is.Empty);
    Assert.That(metadata.TextEntries, Is.Empty);
    Assert.That(metadata.Streams[0].Name, Is.Null);
  }

  // ------------------------------------------------------------------------------------------
  // Helpers
  // ------------------------------------------------------------------------------------------

  private static IReadOnlyList<CodedPacket> _Packets(byte[] container)
    => Mp4Container.ReadPackets(Mp4Reader.FromBytes(container)).ToList();

  /// <summary>Bytes no two of which are alike, so a packet taken from the wrong place is visible.</summary>
  private static byte[] _Payload(int seed, int length) {
    var result = new byte[length];
    for (var i = 0; i < length; ++i)
      result[i] = (byte)(seed * 37 + i * 11 + 1);

    return result;
  }

  /// <summary>Where a box's payload begins, found by its four-character type.</summary>
  private static int _Find(byte[] container, string type) {
    for (var i = 0; i + 8 < container.Length; ++i)
      if (container[i] == type[0] && container[i + 1] == type[1] && container[i + 2] == type[2] && container[i + 3] == type[3])
        return i + 4;

    throw new InvalidOperationException($"no '{type}' box in the built container");
  }

  /// <summary>A JPEG whose picture depends on the seed, so that two frames never look alike.</summary>
  private static byte[] _Jpeg(int seed) {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var i = 0; i < _WIDTH * _HEIGHT; ++i) {
      pixels[i * 3] = (byte)((i * 7 + seed * 61) & 0xFF);
      pixels[i * 3 + 1] = (byte)((i * 3 + seed * 29) & 0xFF);
      pixels[i * 3 + 2] = (byte)((i * 11 + seed * 97) & 0xFF);
    }

    var raw = new RawImage { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };
    return JpegWriter.ToBytes(JpegFile.FromRawImage(raw));
  }
}
