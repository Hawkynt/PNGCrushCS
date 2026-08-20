using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Flv.Tests;

/// <summary>
/// The Flash Video reader's behaviour.
/// </summary>
/// <remarks>
/// Everything asserted here was first measured against ffprobe on files ffmpeg itself wrote, and the
/// numbers in the assertions are the numbers ffprobe reported. Seven files were compared packet for
/// packet — Sorenson Spark alone, AVC alone, Spark with AAC, Spark with MP3, MP3 alone, a longer
/// Spark file and one carrying tags — and the stream index, presentation timestamp, decode timestamp,
/// size and key-frame flag of all ninety-three of their packets match line for line.
/// <para/>
/// The tests that build a file rather than describing one are the shapes ffmpeg will not produce for
/// a file small enough to check by hand: a timestamp past 2^24 milliseconds, a negative composition
/// time, a filtered tag, the extended video header. Each of those is a branch the measured files never
/// reach, and each of them is where a reader that looks right goes wrong.
/// <para/>
/// The reader is a demuxer and nothing else, so what is tested is packets: how many, in what order,
/// how long, and when each is due.
/// </remarks>
[TestFixture]
public sealed class FlvReaderTests {

  // ------------------------------------------------------------------------------------------
  // Opening
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FlvReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FlvReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void WithoutTheSignature_IsRefused() {
    var file = FlvTestContainer.Build([FlvTestContainer.Video(0, _Payload(1, 8))]);
    file[1] = (byte)'X';

    Assert.Throws<InvalidDataException>(() => FlvReader.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void AVersionThisFormatNeverHad_IsRefusedByName() {
    // Only version 1 was ever defined. A file claiming another one is not a later FLV — there is no
    // later FLV — and reading it as though it were would read a header that may not be there.
    var file = FlvTestContainer.Build([FlvTestContainer.Video(0, _Payload(1, 8))], version: 9);

    var failure = Assert.Throws<InvalidDataException>(() => FlvReader.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("9"));
  }

  [Test]
  [Category("Unit")]
  public void ADataOffsetOutsideTheFile_IsRefusedByName() {
    var file = FlvTestContainer.Build([FlvTestContainer.Video(0, _Payload(1, 8))]);
    file[5] = 0x7F;

    var failure = Assert.Throws<InvalidDataException>(() => FlvReader.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("offset"));
  }

  [Test]
  [Category("Unit")]
  public void TheFirstTagIsWhereTheHeaderSaysAndNotNineBytesIn() {
    // The offset is a field rather than a constant, so a writer may put something of its own between
    // the header and the body. A reader that assumed nine would read that something as a tag.
    var padded = FlvTestContainer.Build([FlvTestContainer.Video(0, _Payload(1, 8))], padding: 16);

    Assert.That(_Packets(padded).Select(p => p.Data.Length), Is.EqualTo(new[] { 8 }));
  }

  [Test]
  [Category("Unit")]
  public void TheSignatureIsThreeLettersAndTheVersion() {
    var file = FlvTestContainer.Build([FlvTestContainer.Video(0, _Payload(1, 8))]);

    Assert.That(FlvContainer.MatchesSignature(file), Is.True);
    Assert.That(VideoFormatRegistry.Detect(file), Is.EqualTo(VideoFormat.Flv));
  }

  // ------------------------------------------------------------------------------------------
  // Timestamps
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheTimestampsFourthByteIsItsHighByteAndNotItsLowOne() {
    // The one field of this format that is laid out backwards from the way it is written down: three
    // bytes of timestamp, then the stream id — except that the byte between them is the timestamp's
    // *high* eight bits. A reader that stops at the three is right for the first 2^24 milliseconds,
    // four hours and thirty-six minutes, and then starts every packet over again from zero.
    const long LATE = 0x01_02_03_04;
    var file = FlvTestContainer.Build([
      FlvTestContainer.Video(0x00_FF_FF_FF, _Payload(1, 8)),
      FlvTestContainer.Video(LATE, _Payload(2, 8)),
    ]);

    var packets = _Packets(file);

    Assert.That(packets[0].PresentationTimestamp, Is.EqualTo(0x00FFFFFF));
    Assert.That(packets[1].PresentationTimestamp, Is.EqualTo(LATE));
    Assert.That(packets[1].DecodeTimestamp, Is.EqualTo(LATE));
  }

  [Test]
  [Category("Unit")]
  public void TimestampsAreCountedInMilliseconds() {
    // Fixed by the format, which is why ffprobe reports 1/1000 as the time base of every stream of
    // every FLV — the sound's as well as the pictures'.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Audio(0, _Payload(1, 4)),
      FlvTestContainer.Video(500, _Payload(2, 4)),
    ]);

    var streams = FlvContainer.Streams(FlvReader.FromBytes(file));

    Assert.That(streams.Select(s => s.TimeBase), Is.EqualTo(new[] { new Rational(1, 1000), new Rational(1, 1000) }));
    Assert.That(streams[1].TimeBase.Scale(500), Is.EqualTo(TimeSpan.FromSeconds(0.5)));
  }

  // ------------------------------------------------------------------------------------------
  // Packets
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void EachPacketHoldsThePayloadAndNotTheByteThatNamesTheCodec() {
    // ffprobe reports 1816 bytes for a tag whose data size is 1817. The byte of difference is the
    // frame type and codec, which is the container's framing and not the codec's bytes — a reader
    // that handed it over would disagree with every other tool by one byte on every packet.
    var payloads = new[] { _Payload(1, 10), _Payload(2, 20), _Payload(3, 30) };
    var file = FlvTestContainer.Build(payloads.Select((p, i) => FlvTestContainer.Video(i * 100, p, frameType: i == 0 ? 1 : 2)));

    var packets = _Packets(file);

    Assert.That(packets, Has.Count.EqualTo(3));
    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 10, 20, 30 }));
    for (var i = 0; i < payloads.Length; ++i)
      Assert.That(packets[i].Data.ToArray(), Is.EqualTo(payloads[i]), $"packet {i}");
  }

  [Test]
  [Category("Unit")]
  public void OnlyTheSpecificationsKeyFrameIsAPointDecodingMayBeginAt() {
    // ffprobe flags K on the tags whose frame type is one and on none of the others.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Video(0, _Payload(1, 4), frameType: 1),
      FlvTestContainer.Video(100, _Payload(2, 4), frameType: 2),
      FlvTestContainer.Video(200, _Payload(3, 4), frameType: 3),
    ]);

    Assert.That(_Packets(file).Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, false, false }));
  }

  [Test]
  [Category("Unit")]
  public void AVideoInfoOrCommandFrameIsNotAPacket() {
    // Frame type 5 carries a command to the player rather than a picture. Handing it over would put a
    // unit in the stream that decodes to nothing.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Video(0, _Payload(1, 6)),
      FlvTestContainer.Video(100, _Payload(2, 6), frameType: 5),
      FlvTestContainer.Video(200, _Payload(3, 6), frameType: 2),
    ]);

    Assert.That(_Packets(file).Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 200 }));
  }

  [Test]
  [Category("Unit")]
  public void ATagHoldingNothingButItsOwnHeaderIsNotAPacket() {
    // The same rule the AVI reader applies to a zero-length chunk: nothing is not a frame, and
    // counting it would make the frame count disagree with the oracle's.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Video(0, _Payload(1, 6)),
      FlvTestContainer.Video(100, []),
      FlvTestContainer.Video(200, _Payload(2, 6), frameType: 2),
    ]);

    Assert.That(_Packets(file), Has.Count.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AScriptTagIsNeitherAPacketNorAStream() {
    // ffprobe reports one stream for a file of onMetaData plus video tags, and counts the script tag
    // among neither stream's packets.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Metadata(("duration", 0.5d), ("width", 64d), ("height", 48d)),
      FlvTestContainer.Video(0, _Payload(1, 6)),
    ]);

    var container = FlvReader.FromBytes(file);

    Assert.That(FlvContainer.Streams(container), Has.Count.EqualTo(1));
    Assert.That(FlvContainer.ReadPackets(container).Count(), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ATagOfATypeTheFormatDoesNotDefineIsSteppedOver() {
    // Its length is stated like any other tag's, so the chain survives it. Inventing a stream for it
    // would put a stream in the list that no packet ever belongs to.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Video(0, _Payload(1, 6)),
      new(15, 100, _Payload(2, 12)),
      FlvTestContainer.Video(200, _Payload(3, 6), frameType: 2),
    ]);

    var container = FlvReader.FromBytes(file);

    Assert.That(FlvContainer.Streams(container), Has.Count.EqualTo(1));
    Assert.That(FlvContainer.ReadPackets(container).Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 200 }));
  }

  // ------------------------------------------------------------------------------------------
  // AVC and AAC, whose payloads carry a prefix of their own
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheAvcConfigurationRecordIsTheStreamsPrivateDataAndNotAFrame() {
    // ffprobe reports the 45 bytes of an ffmpeg-written AVC sequence header as this stream's
    // extradata and counts five packets after it, not six. The record describes how the packets are
    // coded and holds no picture at all.
    var configuration = _Payload(9, 45);
    var file = FlvTestContainer.Build([
      FlvTestContainer.Avc(0, configuration, packetType: 0),
      FlvTestContainer.Avc(0, _Payload(1, 20), compositionTime: 200),
      FlvTestContainer.Avc(100, _Payload(2, 12), compositionTime: 500, frameType: 2),
    ]);

    var container = FlvReader.FromBytes(file);

    Assert.That(FlvContainer.Streams(container)[0].CodecPrivateData.ToArray(), Is.EqualTo(configuration));
    Assert.That(FlvContainer.ReadPackets(container).Select(p => p.Data.Length), Is.EqualTo(new[] { 20, 12 }));
  }

  [Test]
  [Category("Unit")]
  public void AnAvcEndOfSequenceIsAMarkerAndNotAFrame() {
    // ffmpeg writes one as the last video tag of every AVC FLV it muxes, with a payload of nothing at
    // all, and ffprobe counts it among neither the packets nor the frames.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Avc(0, _Payload(1, 20)),
      FlvTestContainer.Avc(400, [], packetType: 2),
    ]);

    Assert.That(_Packets(file), Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void AnAvcPacketIsDueAtItsTimestampPlusItsCompositionTime() {
    // Measured on an ffmpeg-written AVC file whose five packets have decode timestamps 0, 100, 200,
    // 300, 400 and composition times 200, 500, 200, 0, 100 — for which ffprobe reports presentation
    // timestamps 200, 600, 400, 300, 500. The two differ exactly where a codec reorders frames.
    var offsets = new[] { 200, 500, 200, 0, 100 };
    var file = FlvTestContainer.Build(offsets.Select((offset, i) =>
      FlvTestContainer.Avc(i * 100, _Payload(i + 1, 8), compositionTime: offset, frameType: i == 0 ? 1 : 2)));

    var packets = _Packets(file);

    Assert.That(packets.Select(p => p.DecodeTimestamp), Is.EqualTo(new long?[] { 0, 100, 200, 300, 400 }));
    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 200, 600, 400, 300, 500 }));
  }

  [Test]
  [Category("Unit")]
  public void ACompositionTimeIsSignedAndMayPutAPictureBeforeItsDecoding() {
    // Twenty-four bits of two's complement. A stream with bidirectional prediction has pictures due
    // before the one being decoded, and a reader that read the field unsigned would put those about
    // four and a half hours late instead of a tenth of a second early.
    var file = FlvTestContainer.Build([FlvTestContainer.Avc(1000, _Payload(1, 8), compositionTime: -100)]);

    var packet = _Packets(file)[0];

    Assert.That(packet.DecodeTimestamp, Is.EqualTo(1000));
    Assert.That(packet.PresentationTimestamp, Is.EqualTo(900));
  }

  [Test]
  [Category("Unit")]
  public void AnAvcTagTooShortForItsOwnPrefixIsRefusedByName() {
    // Five bytes go before an AVC payload. A tag with fewer states a packet type or a composition
    // time that is not there, and reading past the end of it would report a frame made of whatever
    // followed.
    var file = FlvTestContainer.Build([new(FlvTestContainer.VIDEO_TAG, 0, [0x17, 0x01, 0x00])]);

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(file));
    Assert.That(failure!.Message, Does.Contain("AVC"));
  }

  [Test]
  [Category("Unit")]
  public void TheAacAudioSpecificConfigIsTheStreamsPrivateDataAndNotAFrame() {
    // ffprobe reports the five bytes of an ffmpeg-written AAC sequence header as extradata and counts
    // the packets from the tag after it.
    var configuration = _Payload(7, 5);
    var file = FlvTestContainer.Build([
      FlvTestContainer.Aac(0, configuration, packetType: 0),
      FlvTestContainer.Aac(0, _Payload(1, 256)),
      FlvTestContainer.Aac(23, _Payload(2, 258)),
    ]);

    var container = FlvReader.FromBytes(file);

    Assert.That(FlvContainer.Streams(container)[0].CodecPrivateData.ToArray(), Is.EqualTo(configuration));
    Assert.That(FlvContainer.ReadPackets(container).Select(p => p.Data.Length), Is.EqualTo(new[] { 256, 258 }));
  }

  [Test]
  [Category("Unit")]
  public void EveryAudioPacketIsAPointDecodingMayBeginAt() {
    // ffprobe flags K on every audio packet of every FLV measured here, AAC and MP3 alike.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Audio(0, _Payload(1, 100)),
      FlvTestContainer.Audio(26, _Payload(2, 100)),
    ]);

    Assert.That(_Packets(file).Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, true }));
  }

  [Test]
  [Category("Unit")]
  public void AnAudioPacketHoldsThePayloadAndNotTheByteThatNamesTheSoundFormat() {
    var payload = _Payload(4, 153);
    var file = FlvTestContainer.Build([FlvTestContainer.Audio(0, payload)]);

    Assert.That(_Packets(file)[0].Data.ToArray(), Is.EqualTo(payload));
  }

  // ------------------------------------------------------------------------------------------
  // Streams
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void StreamsAreNumberedInTheOrderTheirFirstTagAppears() {
    // ffprobe numbers an FLV's streams this way rather than by the header's flags: a file muxed here
    // with sound and pictures has its audio tag first and comes back as stream 0 audio, stream 1
    // video — which is the opposite of the order the flags list them in.
    var soundFirst = FlvTestContainer.Build([
      FlvTestContainer.Audio(0, _Payload(1, 8)),
      FlvTestContainer.Video(0, _Payload(2, 8)),
    ]);
    var picturesFirst = FlvTestContainer.Build([
      FlvTestContainer.Video(0, _Payload(1, 8)),
      FlvTestContainer.Audio(0, _Payload(2, 8)),
    ]);

    Assert.That(FlvContainer.Streams(FlvReader.FromBytes(soundFirst)).Select(s => s.Kind),
      Is.EqualTo(new[] { MediaStreamKind.Audio, MediaStreamKind.Video }));
    Assert.That(FlvContainer.Streams(FlvReader.FromBytes(picturesFirst)).Select(s => s.Kind),
      Is.EqualTo(new[] { MediaStreamKind.Video, MediaStreamKind.Audio }));
  }

  [Test]
  [Category("Unit")]
  public void TheHeadersFlagsAreNotBelievedAboutWhatTheFileHolds() {
    // They say what the writer intended; the tags say what is there. A stream declared from the flags
    // alone would have no codec and no packets, and one the flags forgot would have every tag of its
    // own belonging to a stream nobody declared.
    var file = FlvTestContainer.Build([FlvTestContainer.Video(0, _Payload(1, 8))], flags: FlvTestContainer.HAS_AUDIO);

    var streams = FlvContainer.Streams(FlvReader.FromBytes(file));

    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
  }

  [Test]
  [Category("Unit")]
  public void OneStreamsPacketsAreWalkedWithoutTheOthers() {
    var file = FlvTestContainer.Build([
      FlvTestContainer.Audio(0, _Payload(1, 10)),
      FlvTestContainer.Video(0, _Payload(2, 20)),
      FlvTestContainer.Audio(23, _Payload(3, 30)),
      FlvTestContainer.Video(100, _Payload(4, 40), frameType: 2),
    ]);

    var container = FlvReader.FromBytes(file);

    Assert.That(FlvContainer.ReadPackets(container, 0).Select(p => p.Data.Length), Is.EqualTo(new[] { 10, 30 }));
    Assert.That(FlvContainer.ReadPackets(container, 1).Select(p => p.Data.Length), Is.EqualTo(new[] { 20, 40 }));
    Assert.That(FlvContainer.ReadPackets(container, 2), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ThePacketsComeOutInTheOrderTheTagsAreStored() {
    // Storage order is playing order here, which is what makes this walk the simplest of the four:
    // an FLV interleaves its sound and its pictures as one chain and each tag carries the moment it
    // is due. ffprobe reports the same alternation for the same file.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Audio(0, _Payload(1, 10)),
      FlvTestContainer.Video(23, _Payload(2, 20)),
      FlvTestContainer.Audio(23, _Payload(3, 30)),
      FlvTestContainer.Audio(46, _Payload(4, 40)),
    ]);

    var packets = _Packets(file);

    Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 0, 1, 0, 0 }));
    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 23, 23, 46 }));
  }

  [TestCase(1, "MJPG")]
  [TestCase(2, "FLV1")]
  [TestCase(3, "FSV1")]
  [TestCase(4, "VP6F")]
  [TestCase(5, "VP6A")]
  [TestCase(6, "FSV2")]
  [TestCase(7, "H264")]
  [Category("Unit")]
  public void AVideoCodecNumberIsReportedUnderTheCodeTheWorldNamesItBy(int code, string expected) {
    // FLV is the only container here that numbers its codecs. An AVI of Sorenson Spark says FLV1 and
    // an FLV of it says 2, and a decoder written against one should not have to know the other's
    // spelling — so the number is translated and kept beside the code as the stream's handler.
    var payload = code == 7 ? _Payload(1, 16) : _Payload(1, 8);
    var tag = code == 7
      ? FlvTestContainer.Avc(0, payload)
      : FlvTestContainer.Video(0, payload, codec: code);

    var stream = FlvContainer.Streams(FlvReader.FromBytes(FlvTestContainer.Build([tag])))[0];

    Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters(expected)));
    Assert.That(stream.Handler, Is.EqualTo(new CodecTag((uint)code)));
  }

  [Test]
  [Category("Unit")]
  public void ASoundFormatWithNoCodeAnybodyAgreesOnKeepsItsNumber() {
    // Flash's own ADPCM has no four-character code, and inventing one would put a name in the model
    // that no other reader would ever produce. The number is still there to be named in a refusal.
    var file = FlvTestContainer.Build([FlvTestContainer.Audio(0, _Payload(1, 8), soundFormat: 1)]);

    var stream = FlvContainer.Streams(FlvReader.FromBytes(file))[0];

    Assert.That(stream.Codec, Is.EqualTo(new CodecTag(1)));
    Assert.That(stream.Handler, Is.EqualTo(new CodecTag(1)));
  }

  [Test]
  [Category("Unit")]
  public void AnAacStreamIsReportedUnderTheCodeTheIsoBaseMediaReaderUses() {
    var file = FlvTestContainer.Build([FlvTestContainer.Aac(0, _Payload(1, 8))]);

    Assert.That(FlvContainer.Streams(FlvReader.FromBytes(file))[0].Codec, Is.EqualTo(CodecTag.FromCharacters("mp4a")));
  }

  // ------------------------------------------------------------------------------------------
  // Refusals
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void ATagDeclaringMoreDataThanTheFileHoldsIsRefusedByName() {
    // An FLV has no index. The only route to the tag after this one is through this one's declared
    // length, so a length that runs past the end leaves everything after it unreachable — and the
    // bytes that are there are part of a unit rather than one. Reporting the packets that came out
    // before it would present a truncated read as a complete one.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Video(0, _Payload(1, 8)),
      FlvTestContainer.Video(100, _Payload(2, 40), frameType: 2),
    ]);
    var cut = file[..^20];

    var failure = Assert.Throws<InvalidDataException>(() => FlvReader.FromBytes(cut));
    Assert.That(failure!.Message, Does.Contain("declares"));
  }

  [Test]
  [Category("Unit")]
  public void AFilteredTagIsRefusedByName() {
    // The payload behind a filter header is the filter's bytes and not the codec's — encrypted, in
    // every case that occurs. Handing it over as a packet would hand over ciphertext as a frame.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Video(0, _Payload(1, 8)),
      FlvTestContainer.Video(100, _Payload(2, 8), frameType: 2, filtered: true),
    ]);

    var failure = Assert.Throws<NotSupportedException>(() => _Packets(file));
    Assert.That(failure!.Message, Does.Contain("filtered"));
  }

  [Test]
  [Category("Unit")]
  public void TheExtendedVideoHeaderIsRefusedByName() {
    // Enhanced RTMP names the codec by a four-character code in a header of its own shape, so the
    // byte a reader would take for a frame type and a codec number is neither. Refusing says which
    // header it is; reading it anyway would report a frame type of nine and a codec of one.
    var file = FlvTestContainer.Build([FlvTestContainer.ExtendedVideo(0, _Payload(1, 16))]);

    var failure = Assert.Throws<NotSupportedException>(() => FlvReader.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("extended header"));
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void OnMetaDataIsWhereTheSizeTheRateAndTheDurationComeFrom() {
    // The exact array ffmpeg writes for a 64x48 film at ten frames a second, read off its own output.
    // None of it is taken from a picture: this is a demuxer and it reports what the file states.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Metadata(
        ("duration", 0.5d), ("width", 64d), ("height", 48d), ("videodatarate", 195.3125d),
        ("framerate", 10d), ("videocodecid", 2d), ("encoder", "Lavf63.1.100"), ("filesize", 2404d)),
      FlvTestContainer.Video(0, _Payload(1, 8)),
    ]);

    var container = FlvReader.FromBytes(file);
    var stream = FlvContainer.Streams(container)[0];
    var metadata = FlvContainer.Metadata(container);

    Assert.That(stream.Width, Is.EqualTo(64));
    Assert.That(stream.Height, Is.EqualTo(48));
    Assert.That(stream.FrameRate, Is.EqualTo(new Rational(10, 1)));
    Assert.That(metadata.Duration, Is.EqualTo(TimeSpan.FromSeconds(0.5)));
    Assert.That(metadata.EncodedBy, Is.EqualTo("Lavf63.1.100"));

    // The measurements are read where they belong and not repeated as annotations.
    Assert.That(metadata.TextEntries, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void TheFlashEraNameForTheEncoderIsReadAsWell() {
    // ffmpeg writes 'encoder'; the tools of the format's own era wrote 'metadatacreator', and a file
    // that only says the latter would otherwise come back saying nothing wrote it.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Metadata(("metadatacreator", "Yet Another Metadata Injector")),
      FlvTestContainer.Video(0, _Payload(1, 8)),
    ]);

    Assert.That(FlvContainer.Metadata(FlvReader.FromBytes(file)).EncodedBy, Is.EqualTo("Yet Another Metadata Injector"));
  }

  [Test]
  [Category("Unit")]
  public void TagsAWriterInventedAreKeptUnderTheNameTheyWereWrittenWith() {
    var file = FlvTestContainer.Build([
      FlvTestContainer.Metadata(("title", "Kurztitel"), ("artist", "Hawkynt"), ("comment", "Testdatei"), ("sourcecamera", "Kamera 3")),
      FlvTestContainer.Video(0, _Payload(1, 8)),
    ]);

    var metadata = FlvContainer.Metadata(FlvReader.FromBytes(file));

    Assert.That(metadata.Title, Is.EqualTo("Kurztitel"));
    Assert.That(metadata.Artist, Is.EqualTo("Hawkynt"));
    Assert.That(metadata.TextEntries.Select(t => t.Keyword), Is.EquivalentTo(new[] { "Comment", "sourcecamera" }));
  }

  [Test]
  [Category("Unit")]
  public void ARateThatIsNotAWholeNumberIsReportedAsTheFileStatesIt() {
    // AMF0 has one number and it is a double, so a rate arrives already rounded. An FLV announcing
    // 29.97 is announcing 2997/100; reporting 30000/1001 instead would report a rate the file never
    // claimed.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Metadata(("framerate", 29.97d)),
      FlvTestContainer.Video(0, _Payload(1, 8)),
    ]);

    Assert.That(FlvContainer.Streams(FlvReader.FromBytes(file))[0].FrameRate, Is.EqualTo(new Rational(2997, 100)));
  }

  [Test]
  [Category("Unit")]
  public void AFileThatSaysNothingAboutItselfCarriesNoInventedMetadata() {
    var file = FlvTestContainer.Build([FlvTestContainer.Video(0, _Payload(1, 8))]);

    var container = FlvReader.FromBytes(file);
    var metadata = FlvContainer.Metadata(container);

    Assert.That(metadata.Title, Is.Null);
    Assert.That(metadata.Artist, Is.Null);
    Assert.That(metadata.EncodedBy, Is.Null);
    Assert.That(metadata.Duration, Is.Null);
    Assert.That(metadata.CreationTime, Is.Null);
    Assert.That(metadata.TextEntries, Is.Empty);
    Assert.That(metadata.Streams, Has.Count.EqualTo(1));

    var stream = FlvContainer.Streams(container)[0];
    Assert.That(stream.Width, Is.Zero);
    Assert.That(stream.Height, Is.Zero);
    Assert.That(stream.FrameRate.IsKnown, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AMessageThatIsNotOnMetaDataIsNotReadAsOne() {
    // onCuePoint and its relatives are messages to a player at a moment in the film rather than
    // statements about the file, and there is nowhere in the model for a thing that happens at a time.
    var file = FlvTestContainer.Build([
      FlvTestContainer.Script("onCuePoint", ("name", "Kapitel 1"), ("time", 1.5d)),
      FlvTestContainer.Video(0, _Payload(1, 8)),
    ]);

    var metadata = FlvContainer.Metadata(FlvReader.FromBytes(file));

    Assert.That(metadata.Duration, Is.Null);
    Assert.That(metadata.TextEntries, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AMalformedOnMetaDataCostsTheAnnotationAndNotTheFilm() {
    // A script tag is annotation. A file whose onMetaData is cut short is still a file whose packets
    // are all there, and refusing it would refuse a film over its title.
    var good = FlvTestContainer.Metadata(("duration", 0.5d), ("title", "Kurztitel"));
    var broken = new FlvTestTag(good.Type, good.Timestamp, good.Data[..^6]);
    var file = FlvTestContainer.Build([broken, FlvTestContainer.Video(0, _Payload(1, 8))]);

    var container = FlvReader.FromBytes(file);

    Assert.That(FlvContainer.Metadata(container).Duration, Is.Null);
    Assert.That(FlvContainer.ReadPackets(container).Count(), Is.EqualTo(1));
  }

  // ------------------------------------------------------------------------------------------
  // Helpers
  // ------------------------------------------------------------------------------------------

  private static IReadOnlyList<CodedPacket> _Packets(byte[] file) => FlvContainer.ReadPackets(FlvReader.FromBytes(file)).ToList();

  /// <summary>Bytes no two of which are alike, so a packet taken from the wrong place is visible.</summary>
  private static byte[] _Payload(int seed, int length) {
    var result = new byte[length];
    for (var i = 0; i < length; ++i)
      result[i] = (byte)(seed * 37 + i * 11 + 1);

    return result;
  }
}
