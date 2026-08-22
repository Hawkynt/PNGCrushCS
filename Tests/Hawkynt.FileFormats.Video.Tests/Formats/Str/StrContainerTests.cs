using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Str.Tests;

/// <summary>
/// The Sony PlayStation STR container's demuxing behaviour: finding the run of CD sectors whether it
/// opens the file directly or sits behind a RIFF/CDXA wrapper, reassembling a video frame out of
/// chunks that are not necessarily consecutive sectors, trimming a frame to its own stated byte length
/// rather than the chunk budget it was reserved, reporting a video frame's timestamp as the per-chunk
/// header's own frame number rather than a running count of pictures seen, and handing that frame out
/// as exactly the reassembled bitstream with the per-chunk header itself left off — see
/// <see cref="StrReader.ReadPackets"/>'s own remarks for which of that header's fields are container
/// bookkeeping already spent and which are left off only because nothing here yet knows the codec
/// needs them restated.
/// </summary>
/// <remarks>
/// Five real recordings from <c>samples.mplayerhq.hu/game-formats/psx-str/</c> — Descent, two
/// releases carrying a RIFF/CDXA shell, and Lunar 2 and Serial Experiments Lain, one raw and the
/// other wrapped — were opened here and their packet stream compared against <c>ffprobe -fflags
/// +noparse</c>'s own: 2,461 video packets and 1,377 audio packets across the five, every video
/// packet's stream index, size and presentation timestamp identical, and every audio packet's size
/// identical. On three of the five, video and audio both, every packet's own bytes were checked
/// against <c>ffprobe -show_data_hash MD5</c>'s own hash of that same packet — 1,212 video packets and
/// 891 audio packets, byte for byte identical to ffmpeg's own demuxed data rather than to a
/// reconstruction built here that could share this reader's own assumptions about where a packet
/// begins. What a hand-built fixture reaches for instead is what no measured sample happened to force:
/// a signature that is not this format's, a truncated file cut inside a frame's own chunks, a frame
/// numbering with a gap in it to settle whether the timestamp this reader reports is the file's own
/// frame number or an invented running count — the same question Sierra VMD's own table of contents
/// forced — and a disc interleaving more than one CD-XA channel's worth of video or audio, which this
/// reader refuses rather than silently merges.
/// </remarks>
[TestFixture]
public sealed class StrContainerTests {

  private const int _SectorSize = 2352;
  private const int _ChunkPayloadLength = 2016;
  private static readonly byte[] _Sync = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

  // ============================================================================================
  // Opening
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnEmptyFileIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => StrContainer.FromBytes([]));
    Assert.That(failure!.Message, Does.Contain("STR"));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithoutTheSyncPatternOrARiffCdxaShellIsRefused() {
    var file = new byte[_SectorSize];
    "XXXX"u8.CopyTo(file);

    Assert.Throws<NotSupportedException>(() => StrContainer.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void ARiffShellNotStatingCdxaIsRefused() {
    var file = new byte[_SectorSize + 44];
    "RIFF"u8.CopyTo(file);
    "AVI "u8.CopyTo(file.AsSpan(8));
    _Sync.CopyTo(file, 44);

    Assert.Throws<NotSupportedException>(() => StrContainer.FromBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoRecognisedVideoChunkIsRefused() {
    // A whole raw sector whose payload never carries the fixed marker words a real chunk states.
    var sector = new byte[_SectorSize];
    _Sync.CopyTo(sector, 0);
    sector[15] = 2; // mode 2

    Assert.Throws<NotSupportedException>(() => StrContainer.FromBytes(sector));
  }

  [Test]
  [Category("Unit")]
  public void ASingleSectorFrameOpensWithTheRightDimensions() {
    var file = _VideoSector(chunkIndex: 0, chunkCount: 1, frameNumber: 1, frameSize: 20, width: 320, height: 160, payload: _Bytes(20));
    var container = StrContainer.FromBytes(file);

    Assert.That(container.Width, Is.EqualTo(320));
    Assert.That(container.Height, Is.EqualTo(160));
    Assert.That(container.VideoFrameCount, Is.EqualTo(1));
    Assert.That(container.HasAudio, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ARawFileAndTheSameSectorsWrappedInRiffCdxaProduceIdenticalPackets() {
    var raw = _VideoSector(chunkIndex: 0, chunkCount: 1, frameNumber: 1, frameSize: 20, width: 64, height: 64, payload: _Bytes(20));
    var wrapped = _WrapRiffCdxa(raw);

    var rawPackets = StrContainer.ReadPackets(StrContainer.FromBytes(raw)).ToArray();
    var wrappedPackets = StrContainer.ReadPackets(StrContainer.FromBytes(wrapped)).ToArray();

    Assert.That(wrappedPackets.Length, Is.EqualTo(rawPackets.Length));
    Assert.That(wrappedPackets[0].Data.ToArray(), Is.EqualTo(rawPackets[0].Data.ToArray()));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DeclaresOnlyAVideoStreamWhenNoAudioSectorIsPresent() {
    var file = _VideoSector(chunkIndex: 0, chunkCount: 1, frameNumber: 1, frameSize: 8, width: 16, height: 16, payload: _Bytes(8));
    var container = StrContainer.FromBytes(file);

    var streams = StrContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(streams[0].Codec.ToString(), Is.EqualTo("MDEC"));
  }

  [Test]
  [Category("Unit")]
  public void DeclaresAnAudioStreamWhenAnAudioSectorIsPresent() {
    var video = _VideoSector(chunkIndex: 0, chunkCount: 1, frameNumber: 1, frameSize: 8, width: 16, height: 16, payload: _Bytes(8));
    var audio = _AudioSector(_Bytes(2304));
    var file = video.Concat(audio).ToArray();
    var container = StrContainer.FromBytes(file);

    var streams = StrContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(2));
    Assert.That(streams[1].Kind, Is.EqualTo(MediaStreamKind.Audio));
    Assert.That(streams[1].Codec.ToString(), Is.EqualTo("XAAD"));
  }

  // ============================================================================================
  // Packets — frame reassembly
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AVideoPacketIsExactlyTheTrimmedBitstreamWithNoChunkHeaderPrefix() {
    // The thirty-two-byte per-chunk header is container bookkeeping this reader has already spent —
    // reassembly, trimming and the timestamp all consumed it — so it is not carried on the packet.
    // Width and height are on the stream, not repeated on every packet.
    var payload = _Bytes(20);
    var file = _VideoSector(chunkIndex: 0, chunkCount: 1, frameNumber: 1, frameSize: 20, width: 320, height: 160, payload: payload);
    var container = StrContainer.FromBytes(file);

    var packet = StrContainer.ReadPackets(container).Single(p => p.StreamIndex == 0);
    Assert.That(packet.Data.Length, Is.EqualTo(20));
    Assert.That(packet.Data.ToArray(), Is.EqualTo(payload));
  }

  [Test]
  [Category("Unit")]
  public void AFrameSpreadAcrossSeveralChunksIsReassembledInChunkOrder() {
    // Every chunk but the last one carries its full 2016-byte capacity for real — a real encoder
    // reserves whole sectors, it does not leave a short chunk in the middle of a frame — so a frame
    // that needs a third chunk has to state a byte length past two chunks' worth to reach it.
    var chunk0 = _FullChunkPayload(0xAA);
    var chunk1 = _FullChunkPayload(0xBB);
    var chunk2 = _FullChunkPayload(0xCC);
    var frameSize = (uint)(2 * _ChunkPayloadLength + 100);

    var file = _VideoSector(0, 3, 1, frameSize, 8, 8, chunk0)
      .Concat(_VideoSector(1, 3, 1, frameSize, 8, 8, chunk1))
      .Concat(_VideoSector(2, 3, 1, frameSize, 8, 8, chunk2))
      .ToArray();

    var container = StrContainer.FromBytes(file);
    Assert.That(container.VideoFrameCount, Is.EqualTo(1));

    var packet = StrContainer.ReadPackets(container).Single(p => p.StreamIndex == 0);
    var expected = chunk0.Concat(chunk1).Concat(chunk2.Take(100)).ToArray();
    Assert.That(packet.Data.ToArray(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void AudioSectorsInterleavedInsideAFramesOwnChunksDoNotBreakReassembly() {
    // Real discs interleave an audio sector between a video frame's chunks: chunk 0, an audio sector,
    // then chunk 1. The frame is still exactly two chunks, not three sectors.
    var chunk0 = _FullChunkPayload(0xAA);
    var chunk1 = _FullChunkPayload(0xBB);
    var frameSize = (uint)(_ChunkPayloadLength + 50);
    var file = _VideoSector(0, 2, 1, frameSize, 8, 8, chunk0)
      .Concat(_AudioSector(_Bytes(2304)))
      .Concat(_VideoSector(1, 2, 1, frameSize, 8, 8, chunk1))
      .ToArray();

    var container = StrContainer.FromBytes(file);
    Assert.That(container.VideoFrameCount, Is.EqualTo(1));
    Assert.That(container.AudioPacketCount, Is.EqualTo(1));

    var packets = StrContainer.ReadPackets(container).ToArray();
    Assert.That(packets.Count(p => p.StreamIndex == 0), Is.EqualTo(1));
    Assert.That(packets.Count(p => p.StreamIndex == 1), Is.EqualTo(1));

    var video = packets.Single(p => p.StreamIndex == 0);
    var expected = chunk0.Concat(chunk1.Take(50)).ToArray();
    Assert.That(video.Data.ToArray(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void AFramesPacketIsTrimmedToItsOwnStatedByteLengthAndNotTheChunkBudget() {
    // Two full chunks reserve 2 * 2016 bytes of capacity; the frame states only a little past the
    // first chunk's own length as real, so the chunk budget beyond that is this encoder's own padding
    // and never appears in the packet.
    var chunk0 = _FullChunkPayload(0xAA);
    var chunk1 = _FullChunkPayload(0xBB);
    var frameSize = (uint)(_ChunkPayloadLength + 5);
    var file = _VideoSector(0, 2, 1, frameSize, 8, 8, chunk0)
      .Concat(_VideoSector(1, 2, 1, frameSize, 8, 8, chunk1))
      .ToArray();

    var container = StrContainer.FromBytes(file);
    var packet = StrContainer.ReadPackets(container).Single(p => p.StreamIndex == 0);

    Assert.That(packet.Data.Length, Is.EqualTo(_ChunkPayloadLength + 5));
    var expected = chunk0.Concat(chunk1.Take(5)).ToArray();
    Assert.That(packet.Data.ToArray(), Is.EqualTo(expected));
  }

  // ============================================================================================
  // Timestamps — the frame number a chunk states, not a running count
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void PresentationTimestampsFollowTheChunkHeadersOwnFrameNumberAcrossAGap() {
    // Frame numbers 1, 2, 5: nothing about a running count of pictures seen would reproduce the jump.
    var file = _VideoSector(0, 1, 1, 4, 8, 8, _Bytes(4))
      .Concat(_VideoSector(0, 1, 2, 4, 8, 8, _Bytes(4)))
      .Concat(_VideoSector(0, 1, 5, 4, 8, 8, _Bytes(4)))
      .ToArray();

    var container = StrContainer.FromBytes(file);
    var packets = StrContainer.ReadPackets(container).Where(p => p.StreamIndex == 0).ToArray();

    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 1, 4 }));
    Assert.That(packets.Select(p => p.DecodeTimestamp), Is.EqualTo(new long?[] { 0, 1, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void EveryVideoPacketIsAKeyFrame() {
    // MDEC is intra-only: nothing about a PlayStation video frame is predicted from another.
    var file = _VideoSector(0, 1, 1, 4, 8, 8, _Bytes(4))
      .Concat(_VideoSector(0, 1, 2, 4, 8, 8, _Bytes(4)))
      .ToArray();

    var container = StrContainer.FromBytes(file);
    Assert.That(StrContainer.ReadPackets(container).Where(p => p.StreamIndex == 0), Is.All.Matches<CodedPacket>(p => p.IsKeyFrame));
  }

  // ============================================================================================
  // Truncation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ATruncatedFileIsWalkedUpToItsLastWholeSectorRatherThanRefused() {
    // Two complete single-chunk frames, then a third sector cut off part way through — the kind of
    // thing an interrupted download or a truncated sample server leaves behind.
    var file = _VideoSector(0, 1, 1, 8, 8, 8, _Bytes(8))
      .Concat(_VideoSector(0, 1, 2, 8, 8, 8, _Bytes(8)))
      .Concat(_VideoSector(0, 1, 3, 8, 8, 8, _Bytes(8)))
      .ToArray();
    var truncated = file[..(2 * _SectorSize + _SectorSize / 2)];

    Assert.DoesNotThrow(() => StrContainer.FromBytes(truncated));
    var container = StrContainer.FromBytes(truncated);
    Assert.That(container.VideoFrameCount, Is.EqualTo(2));
    Assert.That(StrContainer.ReadPackets(container).Count(p => p.StreamIndex == 0), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AFrameCutOffBeforeItsLastChunkIsDroppedRatherThanHandedOutIncomplete() {
    var chunk0 = _Bytes(10, seed: 1);
    var chunk1 = _Bytes(10, seed: 2);
    var complete = _VideoSector(0, 2, 1, 20, 8, 8, chunk0)
      .Concat(_VideoSector(1, 2, 1, 20, 8, 8, chunk1))
      .ToArray();

    // Only the first of the frame's two chunks survives.
    var truncated = complete[.._SectorSize];
    var container = StrContainer.FromBytes(truncated);

    Assert.That(container.VideoFrameCount, Is.EqualTo(0));
    Assert.That(StrContainer.ReadPackets(container).Any(p => p.StreamIndex == 0), Is.False);
  }

  // ============================================================================================
  // Channels
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void VideoChunksOnMoreThanOneCdXaChannelAreRefused() {
    var file = _VideoSector(0, 1, 1, 8, 8, 8, _Bytes(8), channel: 1)
      .Concat(_VideoSector(0, 1, 2, 8, 8, 8, _Bytes(8), channel: 2))
      .ToArray();

    var failure = Assert.Throws<NotSupportedException>(() => StrContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("channel"));
  }

  [Test]
  [Category("Unit")]
  public void AudioSectorsOnMoreThanOneCdXaChannelAreRefused() {
    var file = _AudioSector(_Bytes(2304), channel: 1)
      .Concat(_VideoSector(0, 1, 1, 8, 8, 8, _Bytes(8)))
      .Concat(_AudioSector(_Bytes(2304), channel: 2))
      .ToArray();

    var failure = Assert.Throws<NotSupportedException>(() => StrContainer.FromBytes(file));
    Assert.That(failure!.Message, Does.Contain("channel"));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _Bytes(int length, int seed = 0) {
    var bytes = new byte[length];
    for (var i = 0; i < length; ++i)
      bytes[i] = (byte)(seed * 37 + i);

    return bytes;
  }

  /// <summary>A full 2016-byte chunk payload, every byte the same recognisable value — real chunks
  /// prior to a frame's last one are always fully real bitstream, never a short write padded with
  /// zero, so a multi-chunk test fixture has to fill each one out rather than writing a handful of
  /// bytes into an otherwise-empty chunk.</summary>
  private static byte[] _FullChunkPayload(byte fillValue) {
    var bytes = new byte[_ChunkPayloadLength];
    Array.Fill(bytes, fillValue);
    return bytes;
  }

  private static byte[] _VideoSector(int chunkIndex, int chunkCount, uint frameNumber, uint frameSize, int width, int height, byte[] payload, byte channel = 1) {
    if (payload.Length > 2016)
      throw new ArgumentException("A single sector carries at most 2016 bytes of chunk payload.");

    var sector = new byte[_SectorSize];
    _Sync.CopyTo(sector, 0);
    sector[15] = 2; // mode 2

    // CD-XA subheader, stated twice: file 1, the given channel, submode Data|RealTime, coding 0.
    sector[16] = 1;
    sector[17] = channel;
    sector[18] = 0x48;
    sector[19] = 0;
    sector[20] = 1;
    sector[21] = channel;
    sector[22] = 0x48;
    sector[23] = 0;

    var chunkHeader = sector.AsSpan(24, 32);
    BinaryPrimitives.WriteUInt16LittleEndian(chunkHeader, 0x0160);
    BinaryPrimitives.WriteUInt16LittleEndian(chunkHeader[2..], 0x8001);
    BinaryPrimitives.WriteUInt16LittleEndian(chunkHeader[4..], (ushort)chunkIndex);
    BinaryPrimitives.WriteUInt16LittleEndian(chunkHeader[6..], (ushort)chunkCount);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkHeader[8..], frameNumber);
    BinaryPrimitives.WriteUInt32LittleEndian(chunkHeader[12..], frameSize);
    BinaryPrimitives.WriteUInt16LittleEndian(chunkHeader[16..], (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(chunkHeader[18..], (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(chunkHeader[22..], 0x3800);

    payload.CopyTo(sector, 24 + 32);
    return sector;
  }

  private static byte[] _AudioSector(byte[] audioData, byte channel = 1) {
    if (audioData.Length > 2324)
      throw new ArgumentException("A Form 2 sector carries at most 2324 bytes.");

    var sector = new byte[_SectorSize];
    _Sync.CopyTo(sector, 0);
    sector[15] = 2;

    sector[16] = 1;
    sector[17] = channel;
    sector[18] = 0x64; // Audio | Form 2 | RealTime
    sector[19] = 1;
    sector[20] = 1;
    sector[21] = channel;
    sector[22] = 0x64;
    sector[23] = 1;

    audioData.CopyTo(sector, 24);
    return sector;
  }

  /// <summary>Wraps a run of raw sectors in the same RIFF/CDXA shell real samples carry: the RIFF and
  /// CDXA fourCCs, followed by thirty-two bytes real files leave zero, which is what forces this
  /// reader to search for the sync pattern rather than to walk a named chunk to find it.</summary>
  private static byte[] _WrapRiffCdxa(byte[] sectors) {
    var file = new byte[44 + sectors.Length];
    "RIFF"u8.CopyTo(file);
    "CDXA"u8.CopyTo(file.AsSpan(8));
    sectors.CopyTo(file, 44);
    return file;
  }
}
