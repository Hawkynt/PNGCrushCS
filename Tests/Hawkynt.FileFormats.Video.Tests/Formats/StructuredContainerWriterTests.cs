using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using FileFormat.Asf;
using FileFormat.Core;
using FileFormat.Ea;
using FileFormat.RealMedia;
using FileFormat.RoqVideo;
using FileFormat.Rpl;
using FileFormat.Str;
using FileFormat.Vmd;

namespace Hawkynt.FileFormats.Video.Tests.Formats;

[TestFixture]
public sealed class StructuredContainerWriterTests {

  [Test]
  [Category("Unit")]
  public void StrRoundTripPreservesMdecStateAndXaAudioGeometry() {
    var videoStream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MDEC"),
      Width = 16,
      Height = 16,
    };
    var audioStream = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("XAAD"),
      TimeBase = new Rational(1, 37_800),
      SampleRate = 37_800,
      Channels = 2,
      BitsPerSample = 4,
    };

    var video = Enumerable.Range(0, 12).Select(i => (byte)(0x80 + i)).ToArray();
    var frameState = new byte[12];
    BinaryPrimitives.WriteUInt16LittleEndian(frameState.AsSpan(0), 6);
    BinaryPrimitives.WriteUInt16LittleEndian(frameState.AsSpan(2), 0x3800);
    BinaryPrimitives.WriteUInt16LittleEndian(frameState.AsSpan(4), 10);
    BinaryPrimitives.WriteUInt16LittleEndian(frameState.AsSpan(6), 2);
    var audio = Enumerable.Range(0, 2304).Select(i => (byte)i).ToArray();

    var file = VideoIO.Mux<StrWriter>(
      [videoStream, audioStream],
      [
        new CodedPacket(0, video, PresentationTimestamp: 0, DecodeTimestamp: 0, IsKeyFrame: true, ContainerPrivateData: frameState),
        new CodedPacket(1, audio, IsKeyFrame: true),
      ]);

    Assert.That(file, Has.Length.EqualTo(2 * StrReader.SectorSize));
    Assert.That(file.AsSpan(0x818, 4).ToArray(), Is.Not.EqualTo(new byte[4]));
    Assert.That(file.AsSpan(StrReader.SectorSize + 0x92C, 4).ToArray(), Is.Not.EqualTo(new byte[4]));

    var container = StrContainer.FromBytes(file);
    var streams = StrContainer.Streams(container);
    Assert.That(streams[1].SampleRate, Is.EqualTo(37_800));
    Assert.That(streams[1].Channels, Is.EqualTo(2));
    Assert.That(streams[1].BitsPerSample, Is.EqualTo(4));

    var packets = StrContainer.ReadPackets(container).ToArray();
    var videoPacket = packets.Single(p => p.StreamIndex == 0);
    var audioPacket = packets.Single(p => p.StreamIndex == 1);
    Assert.That(videoPacket.Data.ToArray(), Is.EqualTo(video));
    Assert.That(videoPacket.ContainerPrivateData.ToArray(), Is.EqualTo(frameState));
    Assert.That(audioPacket.Data.ToArray(), Is.EqualTo(audio));
  }

  [Test]
  [Category("Unit")]
  public void EaRoundTripKeepsVideoStateEndAndAudioFamilyChunksInOrder() {
    var videoStream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("cmv "),
      Width = 64,
      Height = 32,
      TimeBase = new Rational(1, 15),
      FrameRate = new Rational(15, 1),
    };
    var audioStream = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("EAAU"),
    };

    var headerPayload = new byte[16];
    BinaryPrimitives.WriteUInt16LittleEndian(headerPayload.AsSpan(4), 64);
    BinaryPrimitives.WriteUInt16LittleEndian(headerPayload.AsSpan(6), 32);
    BinaryPrimitives.WriteUInt16LittleEndian(headerPayload.AsSpan(10), 15);
    var header = _EaChunk("MVIh", headerPayload);
    var sound = _EaChunk("SNDC", [1, 2, 3, 4]);
    var frame = _EaChunk("MVIf", [0, 0, 9, 8, 7]);
    var end = _EaChunk("MVIe", []);

    var file = VideoIO.Mux<EaWriter>(
      [videoStream, audioStream],
      [
        new CodedPacket(0, header),
        new CodedPacket(1, sound, IsKeyFrame: true),
        new CodedPacket(0, frame, PresentationTimestamp: 0, DecodeTimestamp: 0, Duration: 1, IsKeyFrame: true),
        new CodedPacket(0, end),
      ]);

    var container = EaContainer.FromBytes(file);
    Assert.That(container.HasAudio, Is.True);
    var packets = EaContainer.ReadPackets(container).ToArray();
    Assert.That(packets.Select(p => p.Data.ToArray()), Is.EqualTo(new[] { header, sound, frame, end }));
  }

  [Test]
  [Category("Unit")]
  public void RoqRoundTripPreservesSoundPredictorArgumentOutsideCodecBytes() {
    var videoStream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("RoQV"),
      Width = 16,
      Height = 16,
      TimeBase = new Rational(1, 30),
      FrameRate = new Rational(30, 1),
    };
    var audioStream = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("RoQM"),
      TimeBase = new Rational(1, 22_050),
      SampleRate = 22_050,
      Channels = 1,
      BitsPerSample = 16,
    };

    var infoPayload = new byte[8];
    BinaryPrimitives.WriteUInt16LittleEndian(infoPayload, 16);
    BinaryPrimitives.WriteUInt16LittleEndian(infoPayload.AsSpan(2), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(infoPayload.AsSpan(4), 8);
    BinaryPrimitives.WriteUInt16LittleEndian(infoPayload.AsSpan(6), 4);
    var info = _RoqChunk(0x1001, 0, infoPayload);
    var sound = new byte[] { 7, 6, 5, 4 };
    var predictor = new byte[] { 0x34, 0x12 };

    var file = VideoIO.Mux<RoqWriter>(
      [videoStream, audioStream],
      [new CodedPacket(0, info), new CodedPacket(1, sound, ContainerPrivateData: predictor)]);

    var container = RoqContainer.FromBytes(file);
    var audio = RoqContainer.ReadPackets(container).Single(p => p.StreamIndex == 1);
    Assert.That(audio.Data.ToArray(), Is.EqualTo(sound));
    Assert.That(audio.ContainerPrivateData.ToArray(), Is.EqualTo(predictor));
  }

  [Test]
  [Category("Unit")]
  public void RplRoundTripRetainsHeaderAudioGeometryAndChunkPayloads() {
    var videoStream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = new CodecTag(130),
      Width = 64,
      Height = 32,
      BitsPerPixel = 16,
      TimeBase = new Rational(1, 30),
      FrameRate = new Rational(30, 1),
    };
    var audioStream = new MediaStreamInfo {
      Index = 1,
      Kind = MediaStreamKind.Audio,
      Codec = new CodecTag(1),
      TimeBase = new Rational(1, 22_050),
      SampleRate = 22_050,
      Channels = 1,
      BitsPerSample = 16,
    };
    var video = new byte[] { 1, 2, 3 };
    var audio = new byte[] { 9, 8 };

    var file = VideoIO.Mux<RplWriter>(
      [videoStream, audioStream],
      [new CodedPacket(0, video, PresentationTimestamp: 0), new CodedPacket(1, audio, PresentationTimestamp: 0)]);

    var container = RplContainer.FromBytes(file);
    var streams = RplContainer.Streams(container);
    Assert.That(streams[1].SampleRate, Is.EqualTo(22_050));
    Assert.That(streams[1].Channels, Is.EqualTo(1));
    Assert.That(streams[1].BitsPerSample, Is.EqualTo(16));
    var packets = RplContainer.ReadPackets(container).ToArray();
    Assert.That(packets.Single(p => p.StreamIndex == 0).Data.ToArray(), Is.EqualTo(video));
    Assert.That(packets.Single(p => p.StreamIndex == 1).Data.ToArray(), Is.EqualTo(audio));
  }

  [Test]
  [Category("Unit")]
  public void AsfRoundTripRetainsCompleteMediaObjectBytes() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MJPG"),
      Width = 16,
      Height = 16,
      BitsPerPixel = 24,
      TimeBase = new Rational(1, 1000),
      FrameRate = new Rational(25, 1),
    };
    var payload = new byte[] { 0x11, 0x22, 0x33, 0x44 };

    var file = VideoIO.Mux<AsfWriter>(
      [stream],
      [new CodedPacket(0, payload, PresentationTimestamp: 0, DecodeTimestamp: 0, Duration: 40, IsKeyFrame: true)]);

    var container = AsfContainer.FromBytes(file);
    var packet = AsfContainer.ReadPackets(container).Single();
    Assert.That(packet.Data.ToArray(), Is.EqualTo(payload));
    Assert.That(packet.PresentationTimestamp, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RealMediaRoundTripRetainsVideoBytes() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("RV20"),
      Width = 16,
      Height = 16,
      BitsPerPixel = 24,
      TimeBase = new Rational(1, 1000),
      FrameRate = new Rational(15, 1),
      CodecPrivateData = new byte[] { 0xAA, 0xBB },
    };
    var payload = new byte[] { 3, 1, 4, 1, 5 };

    var file = VideoIO.Mux<RealMediaWriter>(
      [stream],
      [new CodedPacket(0, payload, PresentationTimestamp: 0, DecodeTimestamp: 0, IsKeyFrame: true)]);

    var container = RealMediaContainer.FromBytes(file);
    var packet = RealMediaContainer.ReadPackets(container).Single();
    Assert.That(packet.Data.ToArray(), Is.EqualTo(payload));
    Assert.That(packet.IsKeyFrame, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void VmdRoundTripRebuildsTocWithoutChangingRecordPrefixedPacket() {
    var header = new byte[816];
    BinaryPrimitives.WriteUInt16LittleEndian(header, 814);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 16);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), 16);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 816);

    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("VMDV"),
      Width = 16,
      Height = 16,
      CodecPrivateData = header,
    };

    var record = new byte[16];
    record[0] = 2;
    var coded = new byte[] { 10, 20, 30, 40 };
    var packetData = record.Concat(coded).ToArray();

    var file = VideoIO.Mux<VmdWriter>([stream], [new CodedPacket(0, packetData, PresentationTimestamp: 0)]);
    var container = VmdContainer.FromBytes(file);
    var packet = VmdContainer.ReadPackets(container).Single();

    Assert.That(packet.Data.Slice(16).ToArray(), Is.EqualTo(coded));
    Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(packet.Data.Span.Slice(2, 4)), Is.EqualTo((uint)coded.Length));
  }

  private static byte[] _EaChunk(string id, byte[] payload) {
    var result = new byte[8 + payload.Length];
    Encoding.ASCII.GetBytes(id).CopyTo(result, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)result.Length));
    payload.CopyTo(result, 8);
    return result;
  }

  private static byte[] _RoqChunk(ushort id, ushort argument, byte[] payload) {
    var result = new byte[8 + payload.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(result, id);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(2), checked((uint)payload.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), argument);
    payload.CopyTo(result, 8);
    return result;
  }
}
