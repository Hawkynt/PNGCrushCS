using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.MpegPs.Tests;

/// <summary>
/// The MPEG program stream reader's behaviour — <c>.mpg</c>, <c>.mpeg</c>, <c>.vob</c> and
/// <c>.m2p</c> being ISO/IEC 11172-1 and ISO/IEC 13818-1 under four names.
/// </summary>
/// <remarks>
/// Everything asserted here was first measured against ffprobe on files ffmpeg wrote, and the oracle
/// is <c>ffprobe -fflags +nofillin</c> rather than plain ffprobe. That flag matters more than it
/// looks: without it libavformat reports timestamps the container never carried, interpolated from
/// the frame rate — the VOB below whose second packet the file leaves unstamped comes back from plain
/// ffprobe with a presentation timestamp of 63000 — and a demuxer built to match those numbers would
/// be reporting as read what was in fact inferred.
/// <para/>
/// Eleven files were compared packet for packet: MPEG-1 and MPEG-2 program streams, with and without
/// B-pictures, at 64x48 where a PES packet holds seven pictures and at 720x480 where a picture spans
/// several PES packets, with MPEG audio, with MP3, with one AC-3 track and with two. Count, order,
/// size, presentation timestamp, decoding timestamp and keyframe flag agree on every video packet of
/// every one of them, and concatenating the packets of a video stream reproduces the elementary
/// stream ffmpeg extracts from the same file byte for byte.
/// </remarks>
[TestFixture]
public sealed class MpegProgramStreamReaderTests {

  // ------------------------------------------------------------------------------------------
  // Opening
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MpegProgramStreamReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void WithoutAPackHeader_IsRefused() {
    // A program stream is a chain of packs. Something that begins anywhere else has no chain to
    // follow, and reading on from a start code that is not a pack would take a sequence header's
    // first two bytes for a length.
    Assert.Throws<InvalidDataException>(() => MpegProgramStreamReader.FromBytes([0x00, 0x00, 0x01, 0xB3, 0x00, 0x10, 0x00, 0xC0]));
  }

  [Test]
  [Category("Unit")]
  public void TheSignatureIsThePackStartCode() {
    var file = MpegPsTestContainer.Build([_Video(_Frame(1))]);

    Assert.That(MpegProgramStreamContainer.MatchesSignature(file), Is.True);
    Assert.That(VideoFormatRegistry.Detect(file), Is.EqualTo(VideoFormat.MpegProgramStream));
  }

  [Test]
  [Category("Unit")]
  public void ARawElementaryStream_IsNotClaimed() {
    // An .m1v begins with a sequence header, which is a start code and nothing else in common with a
    // program stream. Claiming any start code would claim every raw MPEG video file in existence.
    var elementary = MpegPsTestContainer.Concat(MpegPsTestContainer.SequenceHeader(), MpegPsTestContainer.Picture(1));

    Assert.That(MpegProgramStreamContainer.MatchesSignature(elementary), Is.Null);
    Assert.That(VideoFormatRegistry.Detect(elementary), Is.Not.EqualTo(VideoFormat.MpegProgramStream));
  }

  [Test]
  [Category("Unit")]
  public void APackHeaderInNeitherLayout_IsRefusedByName() {
    // The byte after the start code says which layout the header is, and so how long it is. A value
    // that is neither leaves the next start code's position unknown, and guessing at twelve or at
    // fourteen bytes would lose either the rest of the file or two bytes of the first packet.
    var file = MpegPsTestContainer.Build([_Video(_Frame(1))]);
    file[4] = 0x00;

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(file));
    Assert.That(failure!.Message, Does.Contain("11172-1").And.Contain("13818-1"));
  }

  [TestCase(1)]
  [TestCase(2)]
  [Category("Unit")]
  public void BothSystemsStandards_ReadTheSamePackets(int systemsVersion) {
    // Two pack layouts and two PES header layouts, for the same stream. ffmpeg's `-f mpeg` writes the
    // first pair and its `-f vob` the second, and ffprobe reports the same packets out of both.
    var frames = new[] { _Frame(1), _Frame(2), _Frame(3) };
    var file = MpegPsTestContainer.Build(
      [_Video(frames[0], 45000), _Video(frames[1]), _Video(frames[2])], systemsVersion);

    var container = MpegProgramStreamReader.FromBytes(file);

    Assert.That(container.SystemsVersion, Is.EqualTo(systemsVersion));
    Assert.That(_Packets(file).Select(p => p.Data.ToArray()), Is.EqualTo(frames));
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(7)]
  [Category("Unit")]
  public void PackStuffing_IsNotPayload(int stuffing) {
    // The low three bits of the last byte of an ISO/IEC 13818-1 pack header count the 0xFF bytes that
    // follow it. Counting them as part of the header is the difference between finding the next start
    // code and reading seven bytes of padding as one.
    var frames = new[] { _Frame(1), _Frame(2) };
    var file = MpegPsTestContainer.Build([_Video(frames[0]), _Video(frames[1])], packStuffing: stuffing);

    Assert.That(_Packets(file).Select(p => p.Data.ToArray()), Is.EqualTo(frames));
  }

  [Test]
  [Category("Unit")]
  public void ASystemHeader_IsWalkedPastRatherThanReadAsAPacket() {
    // It is not a PES packet: it has a length and then buffer bounds, with no header in front of them.
    // Read as one, its first two bytes would be taken for flags and everything after it would be lost.
    var frames = new[] { _Frame(1), _Frame(2) };
    var file = MpegPsTestContainer.Build([_Video(frames[0]), _Video(frames[1])], systemHeader: true);

    Assert.That(_Packets(file).Select(p => p.Data.ToArray()), Is.EqualTo(frames));
  }

  [Test]
  [Category("Unit")]
  public void ThePaddingStream_IsNotAPacket() {
    // ffmpeg pads the last pack of every file it muxes out to the pack size; out.mpg ends with 969
    // bytes of it. A reader that took padding for a stream would report a film with an extra track of
    // 0xFF in it.
    var frames = new[] { _Frame(1) };
    var file = MpegPsTestContainer.Build([_Video(frames[0])], padding: 300);

    Assert.That(MpegProgramStreamContainer.Streams(MpegProgramStreamReader.FromBytes(file)), Has.Count.EqualTo(1));
    Assert.That(_Packets(file).Select(p => p.Data.ToArray()), Is.EqualTo(frames));
  }

  [Test]
  [Category("Unit")]
  public void TheProgramEndCode_StopsTheWalk() {
    // Whatever a writer leaves after the end code belongs to no packet. Appending bytes that are not
    // a start code proves the walk stopped rather than merely running out of file.
    var file = MpegPsTestContainer.Build([_Video(_Frame(1))]);
    var trailing = new byte[file.Length + 16];
    file.CopyTo(trailing, 0);
    for (var i = file.Length; i < trailing.Length; ++i)
      trailing[i] = 0x5A;

    Assert.That(_Packets(trailing), Has.Count.EqualTo(1));
  }

  // ------------------------------------------------------------------------------------------
  // Access units
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void SeveralPicturesInOnePesPacket_ComeOutSeparately() {
    // The ordinary case for a small picture: the 64x48 MPEG-1 file ffmpeg wrote holds all thirteen of
    // its pictures in a single PES packet, and ffprobe reports thirteen packets.
    var frames = new[] { _Frame(1), _Frame(2), _Frame(3), _Frame(4) };
    var file = MpegPsTestContainer.Build([_Video(MpegPsTestContainer.Concat(frames), 45000)]);

    var packets = _Packets(file);

    Assert.That(packets.Select(p => p.Data.ToArray()), Is.EqualTo(frames));
  }

  [Test]
  [Category("Unit")]
  public void APictureSpanningSeveralPesPackets_ComesOutAsOnePacket() {
    // The ordinary case for a large one. A 720x480 MPEG-2 picture is several PES packets, and the
    // packet a decoder needs is all of them joined with the container's own bytes taken out.
    var frames = new[] { _Frame(1, 600), _Frame(2, 600) };
    var pieces = MpegPsTestContainer.Split(MpegPsTestContainer.Concat(frames), 400, 400, 250);

    var file = MpegPsTestContainer.Build(
      [_Video(pieces[0], 45000), _Video(pieces[1]), _Video(pieces[2]), _Video(pieces[3])]);

    Assert.That(_Packets(file).Select(p => p.Data.ToArray()), Is.EqualTo(frames));
  }

  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [Category("Unit")]
  public void APictureStartCodeSplitAcrossAPesBoundary_StillEndsThePicture(int bytesInTheFirstPacket) {
    // A start code is four bytes and a PES packet may end in the middle of one. Scanning each payload
    // on its own misses that boundary and hands two pictures over as one packet — which is what a
    // 352x288 stream did against ffprobe until the join was tested explicitly.
    var frames = new[] { _Frame(1, 120), _Frame(2, 120) };
    var whole = MpegPsTestContainer.Concat(frames);
    var pieces = MpegPsTestContainer.Split(whole, 120 + bytesInTheFirstPacket);

    var file = MpegPsTestContainer.Build([_Video(pieces[0], 45000), _Video(pieces[1], 48600)]);

    var packets = _Packets(file);

    Assert.That(packets.Select(p => p.Data.ToArray()), Is.EqualTo(frames));
    // The bytes of the split start code belong to the picture they introduce, not to the one before
    // it, so the first packet is exactly the first picture and no longer.
    Assert.That(packets[0].Data.Length, Is.EqualTo(120));
  }

  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [Category("Unit")]
  public void ASliceStartCodeSplitAcrossAPesBoundary_DoesNotEndThePicture(int bytesInTheFirstPacket) {
    // The mirror of the test above and the reason it was written. A slice start code is inside a
    // picture. Treating any start code at the join as the end of one cut a 352x288 picture into a
    // 57-byte fragment and a 1384-byte remainder where ffprobe reported 735 and 706.
    var frame = MpegPsTestContainer.Concat(
      MpegPsTestContainer.Picture(1, 100), MpegPsTestContainer.Slice(2, 100), MpegPsTestContainer.Slice(3, 100));
    var pieces = MpegPsTestContainer.Split(frame, 100 + bytesInTheFirstPacket);

    var file = MpegPsTestContainer.Build([_Video(pieces[0], 45000), _Video(pieces[1])]);

    var packets = _Packets(file);

    Assert.That(packets, Has.Count.EqualTo(1));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(frame));
  }

  [Test]
  [Category("Unit")]
  public void ASequenceHeaderAndAGroupHeader_BelongToThePictureTheyPrecede() {
    // Both are written in front of the picture they apply to, so cutting after them would put the
    // header of one frame at the end of the packet before it. ffprobe reports the sequence header, the
    // group header and the first picture of every reference file as one packet.
    var first = MpegPsTestContainer.Concat(
      MpegPsTestContainer.SequenceHeader(), MpegPsTestContainer.GroupHeader(), MpegPsTestContainer.Picture(1, 40));
    var second = MpegPsTestContainer.Picture(2, 40);

    var file = MpegPsTestContainer.Build([_Video(MpegPsTestContainer.Concat(first, second), 45000)]);

    Assert.That(_Packets(file).Select(p => p.Data.ToArray()), Is.EqualTo(new[] { first, second }));
  }

  [Test]
  [Category("Unit")]
  public void AnAccessUnitCarryingASequenceHeader_IsWhereDecodingMayBegin() {
    // An I picture on its own states neither the size of the picture nor the quantiser tables, so a
    // decoder started there has nothing to build a frame in. ffprobe flags the same packets on every
    // reference file, because ffmpeg writes a sequence header in front of each of its I pictures.
    var withSequence = MpegPsTestContainer.Concat(MpegPsTestContainer.SequenceHeader(), MpegPsTestContainer.Picture(1, 40));
    var plain = MpegPsTestContainer.Picture(2, 40);
    var again = MpegPsTestContainer.Concat(MpegPsTestContainer.SequenceHeader(), MpegPsTestContainer.Picture(3, 40));

    var file = MpegPsTestContainer.Build([_Video(MpegPsTestContainer.Concat(withSequence, plain, again), 45000)]);

    Assert.That(_Packets(file).Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, false, true }));
  }

  [Test]
  [Category("Unit")]
  public void AGroupHeaderWithNoPictureBehindIt_IsNotAPacket() {
    // The tail of a file that stops after a header. It is part of a picture, not a picture, so it is
    // dropped rather than handed over as a packet of whatever bytes happened to be there.
    var frame = MpegPsTestContainer.Picture(1, 40);
    var file = MpegPsTestContainer.Build(
      [_Video(MpegPsTestContainer.Concat(frame, MpegPsTestContainer.GroupHeader()), 45000)]);

    Assert.That(_Packets(file).Select(p => p.Data.ToArray()), Is.EqualTo(new[] { frame }));
  }

  // ------------------------------------------------------------------------------------------
  // Timing
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void APesHeadersTimestamps_BelongToTheFirstAccessUnitThatCommencesInIt() {
    // And to no other. ffprobe with +nofillin reports exactly one stamped packet per PES packet of the
    // reference VOB — 54000, then nothing, then 72000 where the second PES packet begins.
    var frames = new[] { _Frame(1, 40), _Frame(2, 40), _Frame(3, 40) };
    var file = MpegPsTestContainer.Build([
      _Video(MpegPsTestContainer.Concat(frames[0], frames[1]), 54000, 45000),
      _Video(frames[2], 72000, 63000),
    ]);

    var packets = _Packets(file);

    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 54000, null, 72000 }));
    Assert.That(packets.Select(p => p.DecodeTimestamp), Is.EqualTo(new long?[] { 45000, null, 63000 }));
  }

  [Test]
  [Category("Unit")]
  public void APesHeaderWithOnlyAPresentationTimestamp_IsDecodedAtTheSameMoment() {
    // ISO/IEC 13818-1 defines the decoding time of such an access unit as its presentation time, and
    // ffprobe reports the two as equal for every packet of the B-picture file that carries one stamp.
    var file = MpegPsTestContainer.Build([_Video(_Frame(1), 52200)]);

    var packet = _Packets(file)[0];

    Assert.That(packet.PresentationTimestamp, Is.EqualTo(52200));
    Assert.That(packet.DecodeTimestamp, Is.EqualTo(52200));
  }

  [Test]
  [Category("Unit")]
  public void TimestampsAreCountedInTheSystemClock() {
    var container = MpegProgramStreamReader.FromBytes(MpegPsTestContainer.Build([_Video(_Frame(1), 45000)]));
    var stream = MpegProgramStreamContainer.Streams(container)[0];

    Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, MpegPsTestContainer.SYSTEM_CLOCK_HZ)));
    Assert.That(stream.TimeBase.Scale(45000), Is.EqualTo(TimeSpan.FromSeconds(0.5)));
  }

  [Test]
  [Category("Unit")]
  public void AnIso11172PesHeader_IsReadThroughItsStuffing() {
    // It has no length of its own: stuffing bytes, an optional buffer size and then a code byte are
    // all a reader has, and the only way past them is to read all of them.
    var frame = _Frame(1);
    var file = MpegPsTestContainer.Build([_Video(frame, 45000, 45000)], systemsVersion: 1);

    var packet = _Packets(file)[0];

    Assert.That(packet.Data.ToArray(), Is.EqualTo(frame));
    Assert.That(packet.PresentationTimestamp, Is.EqualTo(45000));
    Assert.That(packet.DecodeTimestamp, Is.EqualTo(45000));
  }

  // ------------------------------------------------------------------------------------------
  // Refusals
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void APacketRunningPastTheEndOfTheFile_IsRefusedRatherThanReturnedShort() {
    // Half a packet is not a packet. Handing back what is there would present a file that stops in
    // the middle of a picture as one that was read to the end.
    var file = MpegPsTestContainer.Build([_Video(_Frame(1, 200), 45000)]);
    var cut = file[..(file.Length - 60)];

    Assert.Throws<InvalidDataException>(() => _Packets(cut));
  }

  [Test]
  [Category("Unit")]
  public void APacketOfNoStatedLength_IsRefusedByName() {
    // An unbounded PES packet is defined only for a transport stream. In a program stream nothing
    // says where it would end, so a reader taking it for "to the next start code" would be inventing
    // a rule the format does not have.
    var file = MpegPsTestContainer.Build([_Video(_Frame(1), 45000)]);
    var at = _FindStartCode(file, MpegPsTestContainer.VIDEO_STREAM);
    file[at + 4] = 0;
    file[at + 5] = 0;

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(file));
    Assert.That(failure!.Message, Does.Contain("transport stream"));
  }

  [Test]
  [Category("Unit")]
  public void AnElementThatIsNotAtAStartCode_IsRefusedWithItsOffset() {
    // Rather than resynchronising to the next start code, which would hand back a file with a hole in
    // it as a file that was read.
    var file = MpegPsTestContainer.Build([_Video(_Frame(1), 45000), _Video(_Frame(2))]);
    var at = _FindStartCode(file, MpegPsTestContainer.VIDEO_STREAM);
    ++file[at + 5]; // one byte too long, so the next pack header is looked for in the wrong place

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(file));
    Assert.That(failure!.Message, Does.Contain("start code"));
  }

  // ------------------------------------------------------------------------------------------
  // Streams
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void EveryStreamIsReportedAndKeepsItsPosition() {
    // Sound as well as pictures, in the order their packets first appear — which is the order a
    // program stream declares anything in, having no header that lists its streams, and the order
    // ffprobe numbers them in for the same file.
    var file = MpegPsTestContainer.Build([
      _Video(_Frame(1), 45000),
      new(MpegPsTestContainer.AUDIO_STREAM, MpegPsTestContainer.Bytes(9, 64), 47618),
    ]);

    var streams = MpegProgramStreamContainer.Streams(MpegProgramStreamReader.FromBytes(file));

    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(streams[1].Codec.ToString(), Is.EqualTo("mpga"));
  }

  [TestCase(1, "mpg1")]
  [TestCase(2, "mpg2")]
  [Category("Unit")]
  public void WithoutAStreamMap_ThePackHeaderDecidesWhichVideoStandardIsNamed(int systemsVersion, string expected) {
    // A program stream states no codec unless it carries a map, and no ffmpeg muxer writes one. What
    // it does state is which systems standard it is, and the two travel together: ffprobe reports
    // mpeg1video for every `-f mpeg` file measured here and mpeg2video for every `-f vob` one.
    var file = MpegPsTestContainer.Build([_Video(_Frame(1), 45000)], systemsVersion);

    var stream = MpegProgramStreamContainer.Streams(MpegProgramStreamReader.FromBytes(file))[0];

    Assert.That(stream.Codec.ToString(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void AProgramStreamMap_OutranksThePackHeader() {
    // The one place a program stream names its codecs. An ISO/IEC 13818-1 file may legally carry
    // ISO/IEC 11172-2 pictures, and where the file says so the file is believed.
    var file = MpegPsTestContainer.Build(
      [_Video(_Frame(1), 45000), new(MpegPsTestContainer.AUDIO_STREAM, MpegPsTestContainer.Bytes(9, 64), 45000)],
      streamMap: [(0x01, MpegPsTestContainer.VIDEO_STREAM), (0x0F, MpegPsTestContainer.AUDIO_STREAM)]);

    var streams = MpegProgramStreamContainer.Streams(MpegProgramStreamReader.FromBytes(file));

    Assert.That(streams[0].Codec.ToString(), Is.EqualTo("mpg1"));
    Assert.That(streams[1].Codec.ToString(), Is.EqualTo("aac "));
  }

  [Test]
  [Category("Unit")]
  public void PrivateStreamOne_IsSplitByItsSubstreamId() {
    // Two languages of AC-3 and a handful of subpicture tracks share stream id 0xBD on a DVD, and
    // nothing but the first byte of the payload tells them apart. ffprobe reports the two AC-3 tracks
    // of a VOB ffmpeg muxed as two streams with ids 0x80 and 0x81.
    var file = MpegPsTestContainer.Build([
      _Video(_Frame(1), 45000),
      _Private(MpegPsTestContainer.AC3_SUBSTREAM, MpegPsTestContainer.Bytes(2, 64), 48078),
      _Private(MpegPsTestContainer.AC3_SUBSTREAM + 1, MpegPsTestContainer.Bytes(3, 64), 48078),
      _Private(MpegPsTestContainer.SUBPICTURE_SUBSTREAM, MpegPsTestContainer.Bytes(4, 32), 48078),
    ]);

    var streams = MpegProgramStreamContainer.Streams(MpegProgramStreamReader.FromBytes(file));

    Assert.That(streams, Has.Count.EqualTo(4));
    Assert.That(streams[1].Codec.ToString(), Is.EqualTo("ac-3"));
    Assert.That(streams[2].Codec.ToString(), Is.EqualTo("ac-3"));
    Assert.That(streams[3].Kind, Is.EqualTo(MediaStreamKind.Subtitle));
  }

  [Test]
  [Category("Unit")]
  public void TheAc3SubstreamHeader_IsNotPartOfThePacket() {
    // Measured: every private packet of the reference VOB begins 80 05 00 01 and the AC-3 sync word
    // follows immediately. Handing those four bytes across would put container bytes at the front of
    // every packet a decoder is given.
    var frames = MpegPsTestContainer.Bytes(5, 64);
    var file = MpegPsTestContainer.Build([
      _Video(_Frame(1), 45000),
      _Private(MpegPsTestContainer.AC3_SUBSTREAM, frames, 48078),
    ]);

    var sound = _Packets(file).Single(p => p.StreamIndex == 1);

    Assert.That(sound.Data.ToArray(), Is.EqualTo(frames));
    Assert.That(sound.PresentationTimestamp, Is.EqualTo(48078));
  }

  [Test]
  [Category("Unit")]
  public void APictureIsHandedOverWhenItIsComplete_WhichIsWhenTheNextOneBegins() {
    // Nothing states a picture's length, so the only thing that ends one is the start of the next —
    // which may be several packets of sound later. A walk that held pictures back to keep them in
    // file order would have to buffer an unbounded number of them, and ffprobe reports the same order
    // for the same reason: its parser completes a frame at the same moment.
    var file = MpegPsTestContainer.Build([
      _Video(_Frame(1), 45000),
      new(MpegPsTestContainer.AUDIO_STREAM, MpegPsTestContainer.Bytes(9, 64), 45000),
      _Video(_Frame(2), 48600),
    ]);

    Assert.That(_Packets(file).Select(p => p.StreamIndex), Is.EqualTo(new[] { 1, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void ASubstreamOfUnknownShape_IsListedAndRefusedByName() {
    // Its packets begin with a header of a length this reader does not know. The stream is still
    // reported, because leaving it out would renumber the others, and asking for its packets says so
    // rather than handing over an unknown number of container bytes.
    var file = MpegPsTestContainer.Build([
      _Video(_Frame(1), 45000),
      _Private(0x55, MpegPsTestContainer.Bytes(6, 32), 48078),
    ]);

    var container = MpegProgramStreamReader.FromBytes(file);
    var streams = MpegProgramStreamContainer.Streams(container);

    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Unknown));

    var failure = Assert.Throws<NotSupportedException>(() => MpegProgramStreamContainer.ReadPackets(container, 1).ToList());
    Assert.That(failure!.Message, Does.Contain("0x55"));
  }

  [Test]
  [Category("Unit")]
  public void SoundIsHandedOverOnePesPacketAtATime() {
    // Where a picture is cut out of the payloads on its own start codes, an MPEG audio frame is found
    // by reading a sampling rate and a bitrate out of the codec's tables — which is the codec's work.
    // ffprobe does split them, using its audio parsers, and its packet list for a stream of sound is
    // accordingly finer than this one.
    var pieces = new[] { MpegPsTestContainer.Bytes(1, 200), MpegPsTestContainer.Bytes(2, 200) };
    var file = MpegPsTestContainer.Build([
      new(MpegPsTestContainer.AUDIO_STREAM, pieces[0], 47618),
      new(MpegPsTestContainer.AUDIO_STREAM, pieces[1], 49969),
    ]);

    var packets = _Packets(file);

    Assert.That(packets.Select(p => p.Data.ToArray()), Is.EqualTo(pieces));
    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 47618, 49969 }));
    Assert.That(packets.Select(p => p.IsKeyFrame), Is.All.True);
  }

  [Test]
  [Category("Unit")]
  public void OneStreamsPacketsCanBeWalkedWithoutTheOthers() {
    var file = MpegPsTestContainer.Build([
      _Video(_Frame(1), 45000),
      new(MpegPsTestContainer.AUDIO_STREAM, MpegPsTestContainer.Bytes(9, 64), 45000),
      _Video(_Frame(2)),
      new(MpegPsTestContainer.AUDIO_STREAM, MpegPsTestContainer.Bytes(8, 64), 47000),
    ]);

    var container = MpegProgramStreamReader.FromBytes(file);

    Assert.That(MpegProgramStreamContainer.ReadPackets(container, 0).Select(p => p.Data.Length), Is.EqualTo(new[] { 32, 32 }));
    Assert.That(MpegProgramStreamContainer.ReadPackets(container, 1).Select(p => p.Data.Length), Is.EqualTo(new[] { 64, 64 }));
    Assert.That(MpegProgramStreamContainer.ReadPackets(container, 9), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AFilteredWalkHandsBackTheSamePacketsAsTheFullOne() {
    // It can, where the AVI reader cannot: every timestamp here comes from a PES header rather than
    // from a running count of the packets that went before, so skipping the other streams changes
    // nothing about what is reported for this one.
    var file = MpegPsTestContainer.Build([
      _Video(MpegPsTestContainer.Concat(_Frame(1), _Frame(2)), 45000),
      new(MpegPsTestContainer.AUDIO_STREAM, MpegPsTestContainer.Bytes(9, 64), 45000),
      _Video(_Frame(3), 52200),
    ]);

    var container = MpegProgramStreamReader.FromBytes(file);
    var filtered = MpegProgramStreamContainer.ReadPackets(container, 0).ToList();
    var whole = MpegProgramStreamContainer.ReadPackets(container).Where(p => p.StreamIndex == 0).ToList();

    Assert.That(filtered.Select(p => p.Data.ToArray()), Is.EqualTo(whole.Select(p => p.Data.ToArray())));
    Assert.That(filtered.Select(p => p.PresentationTimestamp), Is.EqualTo(whole.Select(p => p.PresentationTimestamp)));
  }

  [Test]
  [Category("Unit")]
  public void UnsupportedCodec_StillDemuxes() {
    // The refusal is the codec's and not the container's. A program stream carrying H.264, which
    // nothing here decodes, still comes apart into the packets a remux would move.
    //
    // The stream map is what makes this test say what it means. Without one a video stream is named
    // MPEG-1 or MPEG-2 video from the pack header alone, and both of those now decode — so the file
    // that used to stand for "a container this reads and a codec it does not" has to be one that
    // states a codec outright.
    var container = MpegProgramStreamReader.FromBytes(
      MpegPsTestContainer.Build(
        [_Video(MpegPsTestContainer.Concat(_Frame(1), _Frame(2)), 45000)],
        streamMap: [(_H264_STREAM_TYPE, MpegPsTestContainer.VIDEO_STREAM)]));

    var streams = MpegProgramStreamContainer.Streams(container);

    Assert.That(MpegProgramStreamContainer.ReadPackets(container).Count(), Is.EqualTo(2));
    Assert.That(VideoFormatRegistry.CanDecode(streams[0]), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void UnsupportedCodec_IsRefusedWithItsCodeWhenAPictureIsAskedFor() {
    var file = MpegPsTestContainer.Build(
      [_Video(_Frame(1), 45000)], streamMap: [(_H264_STREAM_TYPE, MpegPsTestContainer.VIDEO_STREAM)]);

    var failure = Assert.Throws<NotSupportedException>(() => VideoFormatRegistry.DecodeFrames(file).ToList());
    Assert.That(failure!.Message, Does.Contain("avc1"));
  }

  [Test]
  [Category("Unit")]
  public void AnMpeg2Stream_HasACodecToDecodeItWith() {
    // The other half of the split, and the reason the two tests above had to be given a codec this
    // library does not read: a program stream naming MPEG-2 video now reaches a decoder.
    var container = MpegProgramStreamReader.FromBytes(
      MpegPsTestContainer.Build([_Video(_Frame(1), 45000)], systemsVersion: 2));

    Assert.That(VideoFormatRegistry.CanDecode(MpegProgramStreamContainer.Streams(container)[0]), Is.True);
  }

  /// <summary>ISO/IEC 13818-1 stream_type for AVC video, which this library has no decoder for.</summary>
  private const byte _H264_STREAM_TYPE = 0x1B;

  [Test]
  [Category("Unit")]
  public void ASoundOnlyFile_DemuxesAndIsRefusedOnlyWhenPicturesAreAskedFor() {
    var file = MpegPsTestContainer.Build([
      new(MpegPsTestContainer.AUDIO_STREAM, MpegPsTestContainer.Bytes(1, 96), 45000),
      new(MpegPsTestContainer.AUDIO_STREAM, MpegPsTestContainer.Bytes(2, 96), 47000),
    ]);

    Assert.That(_Packets(file), Has.Count.EqualTo(2));
    Assert.Throws<InvalidDataException>(() => VideoFormatRegistry.DecodeFrames(file).ToList());
  }

  // ------------------------------------------------------------------------------------------
  // How the packets are handed over
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void APictureInsideOnePesPayload_IsAWindowOntoTheFile() {
    // No copy where none is needed. A demuxer walking a film must not leave a second copy of it
    // behind, and most pictures of a small stream lie inside one payload.
    var bytes = MpegPsTestContainer.Build([_Video(MpegPsTestContainer.Concat(_Frame(1), _Frame(2)), 45000)]);
    var container = MpegProgramStreamReader.FromBytes(bytes);

    foreach (var packet in MpegProgramStreamContainer.ReadPackets(container)) {
      Assert.That(MemoryMarshal.TryGetArray(packet.Data, out var segment), Is.True);
      Assert.That(segment.Array, Is.SameAs(bytes));
    }
  }

  [Test]
  [Category("Unit")]
  public void TheWalkIsLazyAndCanBeRunMoreThanOnce() {
    var frames = new[] { _Frame(1), _Frame(2), _Frame(3) };
    var container = MpegProgramStreamReader.FromBytes(
      MpegPsTestContainer.Build([_Video(MpegPsTestContainer.Concat(frames), 45000)]));

    Assert.That(MpegProgramStreamContainer.ReadPackets(container).First().Data.ToArray(), Is.EqualTo(frames[0]));
    Assert.That(MpegProgramStreamContainer.ReadPackets(container).Count(), Is.EqualTo(3));
    Assert.That(MpegProgramStreamContainer.ReadPackets(container).Skip(1).First().Data.ToArray(), Is.EqualTo(frames[1]));
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void AProgramStreamCarriesNothingAboutItselfButItsStreams() {
    // There is nowhere in the format to write a title, an author or a date, and no field states a
    // duration either — the figure ffprobe reports for one is measured by reading to the end, not
    // declared by the file.
    var metadata = VideoFormatRegistry.ReadMetadata(MpegPsTestContainer.Build([_Video(_Frame(1), 45000)]));

    Assert.That(metadata.Title, Is.Null);
    Assert.That(metadata.Artist, Is.Null);
    Assert.That(metadata.CreationTime, Is.Null);
    Assert.That(metadata.Duration, Is.Null);
    Assert.That(metadata.Streams, Has.Count.EqualTo(1));
    Assert.That(metadata.Streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
  }

  [Test]
  [Category("Unit")]
  public void EveryExtensionTheFormatClaims_ReachesTheSameReader() {
    foreach (var extension in new[] { ".mpg", ".mpeg", ".vob", ".m2p" })
      Assert.That(VideoFormatRegistry.ByExtension(extension), Does.Contain(VideoFormat.MpegProgramStream), extension);
  }

  // ------------------------------------------------------------------------------------------
  // Helpers
  // ------------------------------------------------------------------------------------------

  private static IReadOnlyList<CodedPacket> _Packets(byte[] file)
    => MpegProgramStreamContainer.ReadPackets(MpegProgramStreamReader.FromBytes(file)).ToList();

  private static MpegPsTestPacket _Video(byte[] payload, long? pts = null, long? dts = null)
    => new(MpegPsTestContainer.VIDEO_STREAM, payload, pts, dts);

  /// <summary>A private stream packet, with the four-byte header an AC-3 one carries in front of it.</summary>
  private static MpegPsTestPacket _Private(int substreamId, byte[] payload, long? pts = null)
    => new(MpegPsTestContainer.PRIVATE_STREAM_1, MpegPsTestContainer.Concat([(byte)substreamId, 0x05, 0x00, 0x01], payload), pts);

  private static byte[] _Frame(int seed, int length = 32) => MpegPsTestContainer.Picture(seed, length);

  private static int _FindStartCode(byte[] file, byte streamId) {
    for (var i = 0; i + 4 <= file.Length; ++i)
      if (file[i] == 0 && file[i + 1] == 0 && file[i + 2] == 1 && file[i + 3] == streamId)
        return i;

    throw new InvalidOperationException($"no 00 00 01 {streamId:X2} in the built container");
  }
}
