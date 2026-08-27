using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Str;

/// <summary>
/// Walks a Sony PlayStation STR file's raw CD-XA sectors and exposes reassembled MDEC frames plus
/// XA-ADPCM sectors without decoding either codec.
/// </summary>
internal static class StrReader {

  internal const int SectorSize = 2352;

  private const int _SyncLength = 12;
  private const int _SubheaderOffset = 16;
  private const int _PayloadOffset = 24;
  private const int _ChunkHeaderLength = 32;
  private const int _Form1PayloadLength = 2048;
  private const int _ChunkPayloadLength = _Form1PayloadLength - _ChunkHeaderLength;
  private const int _XaAdpcmDataLength = 2304;
  private const int _RiffPreambleSearchLimit = 2048;

  private const byte _SubmodeAudio = 0x04;
  private const byte _SubmodeForm2 = 0x20;

  private const ushort _ChunkMarker0 = 0x0160;
  private const ushort _ChunkMarker1 = 0x8001;
  private const ushort _ChunkMagic = 0x3800;

  private static readonly byte[] _Sync = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
  private static readonly byte[] _Riff = "RIFF"u8.ToArray();
  private static readonly byte[] _Cdxa = "CDXA"u8.ToArray();

  private static readonly CodecTag _VideoCodec = CodecTag.FromCharacters("MDEC");
  private static readonly CodecTag _AudioCodec = CodecTag.FromCharacters("XAAD");

  internal static CodecTag VideoCodec => _VideoCodec;
  internal static CodecTag AudioCodec => _AudioCodec;

  internal static int LocateSyncStart(ReadOnlySpan<byte> data) {
    if (data.Length >= _SyncLength && data[.._Sync.Length].SequenceEqual(_Sync))
      return 0;

    if (data.Length < 12 || !data[..4].SequenceEqual(_Riff) || !data.Slice(8, 4).SequenceEqual(_Cdxa))
      return -1;

    var searchable = Math.Min(data.Length - 12, _RiffPreambleSearchLimit);
    var found = data.Slice(12, searchable).IndexOf(_Sync);
    return found < 0 ? -1 : 12 + found;
  }

  internal static bool? LooksPlausible(ReadOnlySpan<byte> header) {
    var syncStart = LocateSyncStart(header);
    if (syncStart < 0)
      return false;

    var sectorsAvailable = (header.Length - syncStart) / SectorSize;
    if (sectorsAvailable == 0)
      return null;

    for (var i = 0; i < sectorsAvailable; ++i) {
      var sector = header.Slice(syncStart + i * SectorSize, SectorSize);
      if (!sector[.._Sync.Length].SequenceEqual(_Sync))
        return false;

      var submode = sector[_SubheaderOffset + 2];
      if ((submode & _SubmodeForm2) != 0)
        continue;

      if (_LooksLikeChunkHeader(sector[_PayloadOffset..]))
        return true;
    }

    return null;
  }

  private static bool _LooksLikeChunkHeader(ReadOnlySpan<byte> payload)
    => payload.Length >= _ChunkHeaderLength
       && BinaryPrimitives.ReadUInt16LittleEndian(payload) == _ChunkMarker0
       && BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]) == _ChunkMarker1
       && BinaryPrimitives.ReadUInt16LittleEndian(payload[22..]) == _ChunkMagic;

  internal static StrContainer Open(ReadOnlyMemory<byte> data) {
    var syncStart = LocateSyncStart(data.Span);
    if (syncStart < 0)
      throw new NotSupportedException(
        "This file opens with neither a raw CD sector sync pattern nor a RIFF/CDXA shell. "
        + "This is not a Sony PlayStation STR file this reader recognises.");

    var sectorCount = (data.Length - syncStart) / SectorSize;
    if (sectorCount == 0)
      throw new InvalidDataException(
        $"The sync pattern was found at byte {syncStart}, but fewer than {SectorSize} bytes follow it.");

    var width = 0;
    var height = 0;
    var videoFrameCount = 0;
    var audioPacketCount = 0;
    var audioSampleRate = 0;
    var audioChannels = 0;
    var audioBitsPerSample = 0;
    var anyChunkSeen = false;
    var frameOpen = false;
    var expectedChunks = 0;
    var chunksSeen = 0;
    var videoChannel = -1;
    var audioChannel = -1;

    for (var i = 0; i < sectorCount; ++i) {
      var sector = data.Span.Slice(syncStart + i * SectorSize, SectorSize);
      if (!sector[.._Sync.Length].SequenceEqual(_Sync))
        throw new InvalidDataException(
          $"Sector {i}, at byte {syncStart + i * SectorSize}, does not open with the CD sync pattern.");

      var channel = sector[_SubheaderOffset + 1];
      var submode = sector[_SubheaderOffset + 2];
      if ((submode & _SubmodeForm2) != 0) {
        if ((submode & _SubmodeAudio) != 0) {
          if (audioChannel < 0)
            audioChannel = channel;
          else if (audioChannel != channel)
            throw new NotSupportedException(
              $"Sector {i} is XA audio on channel {channel}, while earlier audio used channel {audioChannel}. "
              + "One STR audio stream cannot represent several interleaved XA channels.");

          var coding = sector[_SubheaderOffset + 3];
          var (sampleRate, channels, bitsPerSample) = _AudioGeometry(coding);
          if (audioPacketCount == 0) {
            audioSampleRate = sampleRate;
            audioChannels = channels;
            audioBitsPerSample = bitsPerSample;
          } else if (audioSampleRate != sampleRate || audioChannels != channels || audioBitsPerSample != bitsPerSample)
            throw new NotSupportedException(
              $"Sector {i} changes XA audio geometry from {audioSampleRate} Hz/{audioChannels}ch/{audioBitsPerSample}-bit "
              + $"to {sampleRate} Hz/{channels}ch/{bitsPerSample}-bit inside one stream.");

          ++audioPacketCount;
        }

        continue;
      }

      var payload = sector[_PayloadOffset..];
      if (!_LooksLikeChunkHeader(payload))
        continue;

      if (videoChannel < 0)
        videoChannel = channel;
      else if (videoChannel != channel)
        throw new NotSupportedException(
          $"Sector {i} is video on CD-XA channel {channel}, while earlier video used channel {videoChannel}. "
          + "One STR video stream cannot represent several interleaved XA channels.");

      anyChunkSeen = true;
      var chunkIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
      var chunkCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);

      if (chunkIndex == 0) {
        if (width == 0 && chunkCount > 0) {
          width = BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]);
          height = BinaryPrimitives.ReadUInt16LittleEndian(payload[18..]);
        }

        expectedChunks = chunkCount;
        chunksSeen = chunkCount > 0 ? 1 : 0;
        frameOpen = chunkCount > 0;
      } else if (frameOpen)
        ++chunksSeen;
      else
        continue;

      if (frameOpen && chunksSeen == expectedChunks) {
        ++videoFrameCount;
        frameOpen = false;
      }
    }

    if (!anyChunkSeen)
      throw new NotSupportedException(
        "No Form-1 sector with the recognised PlayStation STR video chunk header was found.");

    return new() {
      Data = data,
      SyncStart = syncStart,
      SectorCount = sectorCount,
      Width = width,
      Height = height,
      VideoFrameCount = videoFrameCount,
      HasAudio = audioPacketCount > 0,
      AudioPacketCount = audioPacketCount,
      AudioSampleRate = audioSampleRate,
      AudioChannels = audioChannels,
      AudioBitsPerSample = audioBitsPerSample,
    };
  }

  /// <summary>
  /// Walks sectors a second time and emits logical packets. The MDEC payload itself stays free of the
  /// 32-byte STR sector header; the twelve bytes at offsets 20..31 of that header are retained as
  /// <see cref="CodedPacket.ContainerPrivateData"/> because they include the replicated MDEC size,
  /// magic, quantizer and version fields a writer needs for a lossless remux of container state.
  /// </summary>
  internal static IEnumerable<CodedPacket> ReadPackets(StrContainer container) {
    var data = container.Data;
    var syncStart = container.SyncStart;
    var sectorCount = container.SectorCount;

    List<byte[]>? chunks = null;
    byte[]? framePrivate = null;
    var expectedChunks = 0;
    var frameSize = 0u;
    long? firstFrameNumber = null;
    var frameOpen = false;

    for (var i = 0; i < sectorCount; ++i) {
      var sectorOffset = syncStart + i * SectorSize;
      var sector = data.Slice(sectorOffset, SectorSize);
      var submode = sector.Span[_SubheaderOffset + 2];

      if ((submode & _SubmodeForm2) != 0) {
        if ((submode & _SubmodeAudio) != 0 && container.HasAudio)
          yield return new(
            StreamIndex: 1,
            Data: sector.Slice(_PayloadOffset, _XaAdpcmDataLength),
            IsKeyFrame: true);
        continue;
      }

      var payload = sector.Slice(_PayloadOffset);
      if (!_LooksLikeChunkHeader(payload.Span))
        continue;

      var chunkIndex = BinaryPrimitives.ReadUInt16LittleEndian(payload.Span[4..]);
      var chunkCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Span[6..]);

      if (chunkIndex == 0) {
        var frameNumber = (long)BinaryPrimitives.ReadUInt32LittleEndian(payload.Span[8..]);
        firstFrameNumber ??= frameNumber;

        frameSize = BinaryPrimitives.ReadUInt32LittleEndian(payload.Span[12..]);
        expectedChunks = chunkCount;
        chunks = chunkCount > 0 ? [] : null;
        framePrivate = chunkCount > 0 ? payload.Slice(20, 12).ToArray() : null;
        frameOpen = chunkCount > 0;

        if (frameOpen)
          chunks!.Add(payload.Slice(_ChunkHeaderLength, _ChunkPayloadLength).ToArray());

        if (frameOpen && chunks!.Count == expectedChunks) {
          yield return _CompleteFrame(chunks, frameSize, frameNumber - firstFrameNumber.Value, framePrivate!);
          frameOpen = false;
        }
      } else if (frameOpen) {
        chunks!.Add(payload.Slice(_ChunkHeaderLength, _ChunkPayloadLength).ToArray());
        if (chunks.Count == expectedChunks) {
          var frameNumber = (long)BinaryPrimitives.ReadUInt32LittleEndian(payload.Span[8..]);
          yield return _CompleteFrame(chunks, frameSize, frameNumber - firstFrameNumber!.Value, framePrivate!);
          frameOpen = false;
        }
      }
    }
  }

  private static CodedPacket _CompleteFrame(List<byte[]> chunks, uint frameSize, long presentationTimestamp, byte[] privateData) {
    var totalCapacity = 0;
    foreach (var chunk in chunks)
      totalCapacity += chunk.Length;

    var used = (int)Math.Min(frameSize, (uint)totalCapacity);
    var combined = new byte[used];
    var written = 0;
    foreach (var chunk in chunks) {
      if (written >= used)
        break;
      var take = Math.Min(chunk.Length, used - written);
      chunk.AsSpan(0, take).CopyTo(combined.AsSpan(written));
      written += take;
    }

    return new(
      StreamIndex: 0,
      Data: combined,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true,
      ContainerPrivateData: privateData);
  }

  private static (int SampleRate, int Channels, int BitsPerSample) _AudioGeometry(byte codingInformation) {
    var channels = (codingInformation & 0x01) != 0 ? 2 : 1;
    var sampleRate = (codingInformation & 0x04) != 0 ? 18_900 : 37_800;
    var bitsPerSample = (codingInformation & 0x10) != 0 ? 8 : 4;
    return (sampleRate, channels, bitsPerSample);
  }
}
