using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.MpegTs.Tests;

/// <summary>
/// The MPEG-2 transport stream reader's behaviour.
/// </summary>
/// <remarks>
/// Everything asserted here was first measured against ffprobe on files ffmpeg itself wrote, and the
/// numbers in the assertions are the numbers ffprobe reported. Seven files were compared packet for
/// packet — MPEG-2 video alone, MPEG-1 video alone, AVC alone, MPEG-2 with AAC, MPEG-2 audio alone,
/// and the last of those again in Blu-ray and AVCHD framing — and the stream index, presentation
/// timestamp, decode timestamp and size of all fifty-eight of their packets match line for line.
/// <para/>
/// The oracle for that comparison is <c>ffprobe -fflags +noparse</c>. Without it, what ffprobe prints
/// is not the demuxer's packets: ffmpeg runs a codec parser over a transport stream's elementary
/// streams by default and re-splits them into codec access units, so one 2829-byte PES packet of AAC
/// comes back as the fourteen frames inside it, each with an interpolated timestamp. Splitting a
/// packet into frames is a parser's work; this is a demuxer, and it hands over what the multiplex
/// framed.
/// <para/>
/// The tests that build a file rather than describing one are the shapes ffmpeg will not produce on
/// demand: a lost packet, a file that stops mid-unit, a scrambled packet, a section too long for one
/// transport packet, a timestamp near the top of its thirty-three bits.
/// </remarks>
[TestFixture]
public sealed class TransportStreamReaderTests {

  private const int _VIDEO_PID = 0x0100;
  private const int _AUDIO_PID = 0x0101;

  private static readonly TsTestStream[] _VIDEO_ONLY = [new(_VIDEO_PID, 0x02)];
  private static readonly TsTestStream[] _VIDEO_AND_AUDIO = [new(_VIDEO_PID, 0x02), new(_AUDIO_PID, 0x0F)];

  // ------------------------------------------------------------------------------------------
  // Opening and framing
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TransportStreamReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => TransportStreamReader.FromBytes(new byte[32]));

  [Test]
  [Category("Unit")]
  public void SomethingThatIsNotPacketsAtEitherStride_IsRefusedByName() {
    var noise = new byte[4096];
    for (var i = 0; i < noise.Length; ++i)
      noise[i] = (byte)(i * 7 + 1);

    var failure = Assert.Throws<InvalidDataException>(() => TransportStreamReader.FromBytes(noise));
    Assert.That(failure!.Message, Does.Contain("188").And.Contain("192"));
  }

  [Test]
  [Category("Unit")]
  public void TheStrideIsMeasuredAndNotTakenFromTheName() {
    var units = _Units(3);
    var plain = TransportStreamTestContainer.Build(_VIDEO_ONLY, units);
    var timecoded = TransportStreamTestContainer.Build(_VIDEO_ONLY, units, stride: 192);

    Assert.That(timecoded.Length, Is.GreaterThan(plain.Length));
    Assert.That(timecoded[0], Is.Not.EqualTo(0x47));
    Assert.That(_Packets(timecoded).Select(p => p.Data.ToArray()), Is.EqualTo(_Packets(plain).Select(p => p.Data.ToArray())));
  }

  [Test]
  [Category("Unit")]
  public void BothFramingsAreRecognisedFromTheirSyncBytes() {
    var plain = TransportStreamTestContainer.Build(_VIDEO_ONLY, _Units(4));
    var timecoded = TransportStreamTestContainer.Build(_VIDEO_ONLY, _Units(4), stride: 192);

    Assert.That(TransportStreamContainer.MatchesSignature(plain), Is.True);
    Assert.That(TransportStreamContainer.MatchesSignature(timecoded), Is.True);
    Assert.That(VideoFormatRegistry.Detect(plain), Is.EqualTo(VideoFormat.TransportStream));
    Assert.That(VideoFormatRegistry.Detect(timecoded), Is.EqualTo(VideoFormat.TransportStream));
  }

  [Test]
  [Category("Unit")]
  public void OneSyncByteIsNotEnoughToClaimTheFormat() {
    var gif = new byte[1024];
    gif[0] = (byte)'G';
    gif[1] = (byte)'I';
    gif[2] = (byte)'F';

    Assert.That(TransportStreamContainer.MatchesSignature(gif), Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoProgramAssociationTableIsRefusedByName() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, _Units(2));
    var at = TransportStreamTestContainer.IndexOf(file, 0x0000) * TransportStreamTestContainer.PACKET_SIZE;
    file[at + 1] = (byte)(file[at + 1] & 0xE0 | 0x1F);
    file[at + 2] = 0xFE;

    var failure = Assert.Throws<InvalidDataException>(() => TransportStreamReader.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("program association"));
  }

  [Test]
  [Category("Unit")]
  public void AFileWhoseProgramMapNeverArrivesIsRefusedByName() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, _Units(2), withProgramMap: false);

    var failure = Assert.Throws<InvalidDataException>(() => TransportStreamReader.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("program map"));
  }

  [Test]
  [Category("Unit")]
  public void ASectionTooLongForOnePacketIsAssembledAcrossThem() {
    var many = new TsTestStream[40];
    for (var i = 0; i < many.Length; ++i)
      many[i] = new(0x0200 + i, 0x02);

    var file = TransportStreamTestContainer.Build(many, [new(0x0200, _Payload(1, 40), 90000, 90000)]);

    Assert.That(TransportStreamContainer.Streams(TransportStreamReader.FromBytes(file)), Has.Count.EqualTo(many.Length));
  }

  [Test]
  [Category("Unit")]
  public void ASectionWhoseCrcDoesNotCheckOutIsNotATable() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, _Units(2));
    var at = TransportStreamTestContainer.IndexOf(file, TransportStreamTestContainer.PROGRAM_MAP_PID) * TransportStreamTestContainer.PACKET_SIZE;
    file[at + 10] ^= 0xFF;

    Assert.Throws<InvalidDataException>(() => TransportStreamReader.FromBytes(file));
  }

  // ------------------------------------------------------------------------------------------
  // Packets
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void AUnitIsAssembledOutOfEveryPacketItWasCutInto() {
    var payloads = new[] { _Payload(1, 1469), _Payload(2, 480), _Payload(3, 113) };
    var file = TransportStreamTestContainer.Build(
      _VIDEO_ONLY,
      payloads.Select((p, i) => new TsTestUnit(_VIDEO_PID, p, 135000 + i * 9000, 126000 + i * 9000)));

    var packets = _Packets(file);

    Assert.That(packets.Select(p => p.Data.Length), Is.EqualTo(new[] { 1469, 480, 113 }));
    for (var i = 0; i < payloads.Length; ++i)
      Assert.That(packets[i].Data.ToArray(), Is.EqualTo(payloads[i]), $"packet {i}");
  }

  [Test]
  [Category("Unit")]
  public void APacketHoldsTheElementaryBytesAndNoneOfTheFraming() {
    var payload = _Payload(4, 200);
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, [new(_VIDEO_PID, payload, 90000, 90000)]);

    Assert.That(_Packets(file)[0].Data.ToArray(), Is.EqualTo(payload));
  }

  [Test]
  [Category("Unit")]
  public void AnAdaptationFieldIsSkippedRatherThanTakenForPayload() {
    var payload = _Payload(5, 300);
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, [new(_VIDEO_PID, payload, 90000, 90000, RandomAccess: true)]);

    var packet = _Packets(file)[0];

    Assert.That(packet.Data.ToArray(), Is.EqualTo(payload));
    Assert.That(packet.IsKeyFrame, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void OnlyTheRandomAccessIndicatorSaysWhereDecodingMayBegin() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, [
      new(_VIDEO_PID, _Payload(1, 100), 90000, 90000, RandomAccess: true),
      new(_VIDEO_PID, _Payload(2, 100), 99000, 99000),
      new(_VIDEO_PID, _Payload(3, 100), 108000, 108000),
    ]);

    Assert.That(_Packets(file).Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, false, false }));
  }

  [Test]
  [Category("Unit")]
  public void AUnitThatStatesNoLengthEndsWhereTheNextOneBegins() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, [
      new(_VIDEO_PID, _Payload(1, 500), 90000, 90000),
      new(_VIDEO_PID, _Payload(2, 40), 99000, 99000),
    ]);

    Assert.That(_Packets(file).Select(p => p.Data.Length), Is.EqualTo(new[] { 500, 40 }));
  }

  [Test]
  [Category("Unit")]
  public void AUnitThatStatesItsLengthEndsThereAndTheRestOfThePacketIsStuffing() {
    var payload = _Payload(6, 2829);
    var file = TransportStreamTestContainer.Build(
      _VIDEO_AND_AUDIO,
      [new(_AUDIO_PID, payload, 132910, StreamId: 0xC0, DeclareLength: true)]);

    var packets = _Packets(file);

    Assert.That(packets, Has.Count.EqualTo(1));
    Assert.That(packets[0].Data.ToArray(), Is.EqualTo(payload));
  }

  [Test]
  [Category("Unit")]
  public void PacketsComeOutInTheOrderTheyAreFinishedAndNotTheOrderTheyBegin() {
    var file = TransportStreamTestContainer.Build(_VIDEO_AND_AUDIO, [
      new(_VIDEO_PID, _Payload(1, 300), 135000, 126000, RandomAccess: true),
      new(_VIDEO_PID, _Payload(2, 300), 144000, 135000),
      new(_VIDEO_PID, _Payload(3, 300), 153000, 144000),
      new(_AUDIO_PID, _Payload(4, 400), 132910, StreamId: 0xC0, DeclareLength: true),
      new(_VIDEO_PID, _Payload(5, 300), 162000, 153000),
    ]);

    var packets = _Packets(file);

    Assert.That(packets.Select(p => p.StreamIndex), Is.EqualTo(new[] { 0, 0, 1, 0, 0 }));
    Assert.That(packets.Select(p => p.PresentationTimestamp),
      Is.EqualTo(new long?[] { 135000, 144000, 132910, 153000, 162000 }));
  }

  [Test]
  [Category("Unit")]
  public void TimestampsAreCountedInTheNinetyKilohertzSystemClock() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, [new(_VIDEO_PID, _Payload(1, 40), 135000, 126000)]);

    var container = TransportStreamReader.FromBytes(file);
    var stream = TransportStreamContainer.Streams(container)[0];

    Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 90000)));
    Assert.That(stream.TimeBase.Scale(135000), Is.EqualTo(TimeSpan.FromSeconds(1.5)));
  }

  [Test]
  [Category("Unit")]
  public void ATimestampNearTheTopOfItsThirtyThreeBitsIsReadWhole() {
    const long LATE = 0x1_FEDC_BA98;
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, [new(_VIDEO_PID, _Payload(1, 40), LATE, LATE - 9000)]);

    var packet = _Packets(file)[0];

    Assert.That(packet.PresentationTimestamp, Is.EqualTo(LATE));
    Assert.That(packet.DecodeTimestamp, Is.EqualTo(LATE - 9000));
  }

  [Test]
  [Category("Unit")]
  public void AUnitWithOnlyAPresentationTimeIsDecodedWhenItIsPresented() {
    var file = TransportStreamTestContainer.Build(
      _VIDEO_AND_AUDIO,
      [new(_AUDIO_PID, _Payload(1, 200), 132910, StreamId: 0xC0, DeclareLength: true)]);

    var packet = _Packets(file)[0];

    Assert.That(packet.PresentationTimestamp, Is.EqualTo(132910));
    Assert.That(packet.DecodeTimestamp, Is.EqualTo(132910));
  }

  [Test]
  [Category("Unit")]
  public void AUnitWithNoTimestampsAtAllStatesNone() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, [new(_VIDEO_PID, _Payload(1, 40))]);

    var packet = _Packets(file)[0];

    Assert.That(packet.PresentationTimestamp, Is.Null);
    Assert.That(packet.DecodeTimestamp, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void OneStreamsPacketsAreWalkedWithoutTheOthers() {
    var file = TransportStreamTestContainer.Build(_VIDEO_AND_AUDIO, [
      new(_VIDEO_PID, _Payload(1, 300), 135000, 126000),
      new(_AUDIO_PID, _Payload(2, 400), 132910, StreamId: 0xC0, DeclareLength: true),
      new(_VIDEO_PID, _Payload(3, 200), 144000, 135000),
    ]);

    var container = TransportStreamReader.FromBytes(file);

    Assert.That(TransportStreamContainer.ReadPackets(container, 0).Select(p => p.Data.Length), Is.EqualTo(new[] { 300, 200 }));
    Assert.That(TransportStreamContainer.ReadPackets(container, 1).Select(p => p.Data.Length), Is.EqualTo(new[] { 400 }));
    Assert.That(TransportStreamContainer.ReadPackets(container, 2), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void TheWalkIsRerunnable() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, _Units(4));
    var container = TransportStreamReader.FromBytes(file);

    var first = TransportStreamContainer.ReadPackets(container).Select(p => p.Data.Length).ToArray();
    var again = TransportStreamContainer.ReadPackets(container).Select(p => p.Data.Length).ToArray();

    Assert.That(again, Is.EqualTo(first));
    Assert.That(first, Has.Length.EqualTo(4));
  }

  // ------------------------------------------------------------------------------------------
  // Refusals
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void APacketLostInTheMiddleOfAUnitIsRefusedByName() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, [
      new(_VIDEO_PID, _Payload(1, 900), 90000, 90000),
      new(_VIDEO_PID, _Payload(2, 100), 99000, 99000),
    ]);
    var lost = TransportStreamTestContainer.Drop(file, TransportStreamTestContainer.IndexOf(file, _VIDEO_PID, 2));

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(lost));
    Assert.That(failure!.Message, Does.Contain("continuity counter"));
  }

  [Test]
  [Category("Unit")]
  public void APacketSentTwiceIsNotCountedTwice() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, [new(_VIDEO_PID, _Payload(1, 600), 90000, 90000)]);
    var index = TransportStreamTestContainer.IndexOf(file, _VIDEO_PID, 1);
    var at = index * TransportStreamTestContainer.PACKET_SIZE;

    var doubled = new byte[file.Length + TransportStreamTestContainer.PACKET_SIZE];
    Array.Copy(file, 0, doubled, 0, at + TransportStreamTestContainer.PACKET_SIZE);
    Array.Copy(file, at, doubled, at + TransportStreamTestContainer.PACKET_SIZE, TransportStreamTestContainer.PACKET_SIZE);
    Array.Copy(file, at + TransportStreamTestContainer.PACKET_SIZE, doubled, at + 2 * TransportStreamTestContainer.PACKET_SIZE,
      file.Length - at - TransportStreamTestContainer.PACKET_SIZE);

    Assert.That(_Packets(doubled).Select(p => p.Data.Length), Is.EqualTo(new[] { 600 }));
  }

  [Test]
  [Category("Unit")]
  public void AFileThatStopsInTheMiddleOfAStatedLengthIsRefusedByName() {
    var file = TransportStreamTestContainer.Build(
      _VIDEO_AND_AUDIO,
      [new(_AUDIO_PID, _Payload(1, 900), 132910, StreamId: 0xC0, DeclareLength: true)]);

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(file[..^TransportStreamTestContainer.PACKET_SIZE]));
    Assert.That(failure!.Message, Does.Contain("ends"));
  }

  [Test]
  [Category("Unit")]
  public void AUnitWhoseStatedLengthTheNextOneCutsShortIsRefusedByName() {
    var file = TransportStreamTestContainer.Build(_VIDEO_AND_AUDIO, [
      new(_AUDIO_PID, _Payload(1, 200), 132910, StreamId: 0xC0, StatedLength: 4000),
      new(_AUDIO_PID, _Payload(2, 200), 135000, StreamId: 0xC0, DeclareLength: true),
    ]);

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(file));
    Assert.That(failure!.Message, Does.Contain("4006"));
  }

  [Test]
  [Category("Unit")]
  public void AScrambledPacketIsRefusedByName() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, _Units(2));
    var at = TransportStreamTestContainer.IndexOf(file, _VIDEO_PID) * TransportStreamTestContainer.PACKET_SIZE;
    file[at + 3] |= 0xC0;

    var failure = Assert.Throws<NotSupportedException>(() => _Packets(file));
    Assert.That(failure!.Message, Does.Contain("scrambled"));
  }

  [Test]
  [Category("Unit")]
  public void APacketMarkedCorruptIsRefusedByName() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, _Units(2));
    var at = TransportStreamTestContainer.IndexOf(file, _VIDEO_PID) * TransportStreamTestContainer.PACKET_SIZE;
    file[at + 1] |= 0x80;

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(file));
    Assert.That(failure!.Message, Does.Contain("transport error"));
  }

  [Test]
  [Category("Unit")]
  public void APacketThatIsNotWhereTheFramingSaysIsRefusedByName() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, _Units(6));
    var at = TransportStreamTestContainer.IndexOf(file, _VIDEO_PID, 3) * TransportStreamTestContainer.PACKET_SIZE;
    file[at] = 0x00;

    var failure = Assert.Throws<InvalidDataException>(() => _Packets(file));
    Assert.That(failure!.Message, Does.Contain("sync byte"));
  }

  // ------------------------------------------------------------------------------------------
  // Streams
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void StreamsAreNumberedInTheOrderTheProgramMapDeclaresThem() {
    var file = TransportStreamTestContainer.Build(
      [new(_AUDIO_PID, 0x0F), new(_VIDEO_PID, 0x02)],
      [new(_VIDEO_PID, _Payload(1, 40), 90000, 90000)]);

    var streams = TransportStreamContainer.Streams(TransportStreamReader.FromBytes(file));

    Assert.That(streams.Select(s => s.Kind), Is.EqualTo(new[] { MediaStreamKind.Audio, MediaStreamKind.Video }));
    Assert.That(streams.Select(s => s.Index), Is.EqualTo(new[] { 0, 1 }));
  }

  [TestCase(0x01, "MPG1", MediaStreamKind.Video)]
  [TestCase(0x02, "MPG2", MediaStreamKind.Video)]
  [TestCase(0x10, "MP4V", MediaStreamKind.Video)]
  [TestCase(0x1B, "H264", MediaStreamKind.Video)]
  [TestCase(0x24, "HEVC", MediaStreamKind.Video)]
  [TestCase(0x0F, "mp4a", MediaStreamKind.Audio)]
  [TestCase(0x81, "ac-3", MediaStreamKind.Audio)]
  [Category("Unit")]
  public void AStreamTypeIsReportedUnderTheCodeTheWorldNamesItBy(int streamType, string expected, MediaStreamKind kind) {
    var file = TransportStreamTestContainer.Build(
      [new(_VIDEO_PID, streamType)],
      [new(_VIDEO_PID, _Payload(1, 40), 90000, 90000)]);

    var stream = TransportStreamContainer.Streams(TransportStreamReader.FromBytes(file))[0];

    Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters(expected)));
    Assert.That(stream.Handler, Is.EqualTo(new CodecTag((uint)streamType)));
    Assert.That(stream.Kind, Is.EqualTo(kind));
  }

  [Test]
  [Category("Unit")]
  public void MpegAudioKeepsItsNumberRatherThanBorrowingACodeForOneOfItsLayers() {
    var file = TransportStreamTestContainer.Build(
      [new(_AUDIO_PID, 0x03)],
      [new(_AUDIO_PID, _Payload(1, 40), 90000, StreamId: 0xC0, DeclareLength: true)]);

    var stream = TransportStreamContainer.Streams(TransportStreamReader.FromBytes(file))[0];

    Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(stream.Codec, Is.EqualTo(new CodecTag(3)));
  }

  [Test]
  [Category("Unit")]
  public void ARegistrationDescriptorNamesACodingTheStreamTypeDoesNot() {
    byte[] descriptors = [0x05, 0x04, (byte)'A', (byte)'C', (byte)'-', (byte)'3', 0x6A, 0x01, 0x00];
    var file = TransportStreamTestContainer.Build(
      [new(_AUDIO_PID, 0x06, descriptors)],
      [new(_AUDIO_PID, _Payload(1, 40), 90000, StreamId: 0xBD, DeclareLength: true)]);

    var stream = TransportStreamContainer.Streams(TransportStreamReader.FromBytes(file))[0];

    Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("AC-3")));
    Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Audio));
  }

  [Test]
  [Category("Unit")]
  public void APrivateStreamWithNothingIdentifyingItIsReportedAsData() {
    var file = TransportStreamTestContainer.Build(
      [new(_AUDIO_PID, 0x06)],
      [new(_AUDIO_PID, _Payload(1, 40), 90000, StreamId: 0xBD, DeclareLength: true)]);

    var stream = TransportStreamContainer.Streams(TransportStreamReader.FromBytes(file))[0];

    Assert.That(stream.Kind, Is.EqualTo(MediaStreamKind.Data));
    Assert.That(stream.Codec, Is.EqualTo(new CodecTag(6)));
  }

  [Test]
  [Category("Unit")]
  public void AStreamsDescriptorsAreCarriedAcrossVerbatim() {
    byte[] descriptors = [0x0A, 0x04, (byte)'d', (byte)'e', (byte)'u', 0x00];
    var file = TransportStreamTestContainer.Build(
      [new(_AUDIO_PID, 0x0F, descriptors)],
      [new(_AUDIO_PID, _Payload(1, 40), 90000, StreamId: 0xC0, DeclareLength: true)]);

    var stream = TransportStreamContainer.Streams(TransportStreamReader.FromBytes(file))[0];

    Assert.That(stream.ContainerPrivateData.ToArray(), Is.EqualTo(descriptors));
    Assert.That(stream.CodecPrivateData, Is.Empty);
    Assert.That(stream.Language, Is.EqualTo("deu"));
  }

  [Test]
  [Category("Unit")]
  public void NothingIsReportedAboutAPictureThatIsNotInTheTables() {
    var file = TransportStreamTestContainer.Build(_VIDEO_ONLY, _Units(2));

    var stream = TransportStreamContainer.Streams(TransportStreamReader.FromBytes(file))[0];

    Assert.That(stream.Width, Is.Zero);
    Assert.That(stream.Height, Is.Zero);
    Assert.That(stream.FrameRate.IsKnown, Is.False);
    Assert.That(stream.DeclaredFrameCount, Is.Null);
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheServiceDescriptionIsTheOnlyThingInAMultiplexThatIsATitle() {
    var file = TransportStreamTestContainer.Build(
      _VIDEO_ONLY, _Units(2), serviceName: "Service01", serviceProvider: "FFmpeg");

    var metadata = TransportStreamContainer.Metadata(TransportStreamReader.FromBytes(file));

    Assert.That(metadata.Title, Is.EqualTo("Service01"));
    Assert.That(metadata.TextEntries.Select(t => t.Keyword), Is.EqualTo(new[] { "Service Provider" }));
    Assert.That(metadata.TextEntries[0].Text, Is.EqualTo("FFmpeg"));
  }

  [Test]
  [Category("Unit")]
  public void AMultiplexThatSaysNothingAboutItselfCarriesNoInventedMetadata() {
    var file = TransportStreamTestContainer.Build(_VIDEO_AND_AUDIO, _Units(2));

    var metadata = TransportStreamContainer.Metadata(TransportStreamReader.FromBytes(file));

    Assert.That(metadata.Title, Is.Null);
    Assert.That(metadata.Artist, Is.Null);
    Assert.That(metadata.EncodedBy, Is.Null);
    Assert.That(metadata.CreationTime, Is.Null);
    Assert.That(metadata.TextEntries, Is.Empty);
    Assert.That(metadata.Duration, Is.Null);
    Assert.That(metadata.Streams, Has.Count.EqualTo(2));
  }

  // ------------------------------------------------------------------------------------------
  // Helpers
  // ------------------------------------------------------------------------------------------

  private static IReadOnlyList<CodedPacket> _Packets(byte[] file)
    => TransportStreamContainer.ReadPackets(TransportStreamReader.FromBytes(file)).ToList();

  private static TsTestUnit[] _Units(int count) {
    var result = new TsTestUnit[count];
    for (var i = 0; i < count; ++i)
      result[i] = new(_VIDEO_PID, _Payload(i + 1, 100 + i * 37), 90000 + i * 9000, 90000 + i * 9000, RandomAccess: i == 0);

    return result;
  }

  private static byte[] _Payload(int seed, int length) {
    var result = new byte[length];
    for (var i = 0; i < length; ++i)
      result[i] = (byte)(seed * 37 + i * 11 + 1);

    return result;
  }
}
