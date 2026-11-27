using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ea;

/// <summary>Splits an Electronic Arts multimedia file into its self-delimiting chunks.</summary>
internal static class EaReader {

  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _CMV_HEADER_MIN_LENGTH = 0x10;
  private const int _TGV_HEADER_MIN_LENGTH = 0x0C;

  internal readonly record struct ChunkHeader(uint FourCc, int PayloadLength, int PayloadOffset) {
    internal int ChunkStart => this.PayloadOffset - _CHUNK_HEADER_LENGTH;
    internal int ChunkLength => this.PayloadLength + _CHUNK_HEADER_LENGTH;
  }

  internal static bool LooksPlausible(ReadOnlySpan<byte> header) {
    if (header.Length < _CHUNK_HEADER_LENGTH)
      return false;
    var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(header);
    if (!_IsKnownChunk(fourCc))
      return false;
    var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
    return Math.Min(size, BinaryPrimitives.ReverseEndianness(size)) >= _CHUNK_HEADER_LENGTH;
  }

  private static bool _IsKnownChunk(uint fourCc)
    => EaChunkType.IsCmv(fourCc) || EaChunkType.IsTgv(fourCc) || EaChunkType.IsMad(fourCc)
       || EaChunkType.IsTgq(fourCc) || fourCc == EaChunkType.pIQT || EaChunkType.IsAudio(fourCc);

  /// <summary>
  /// Whether the chunk sizes are big-endian, as the console releases write them: FFmpeg's test, the
  /// first chunk's size larger read one way than the other.
  /// </summary>
  internal static bool IsBigEndian(ReadOnlySpan<byte> data) {
    if (data.Length < _CHUNK_HEADER_LENGTH)
      return false;
    var size = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
    return size > BinaryPrimitives.ReverseEndianness(size);
  }

  internal static EaContainer Open(ReadOnlyMemory<byte> data) {
    if (!LooksPlausible(data.Span))
      throw new NotSupportedException(
        "The file does not open with a recognisable Electronic Arts video or audio chunk stating a plausible size.");

    var summary = _Summarise(data);
    return new() {
      Data = data,
      VideoCodec = summary.VideoCodec,
      Width = summary.Width,
      Height = summary.Height,
      FrameRate = summary.FrameRate,
      VideoFrameCount = summary.VideoFrameCount,
      HasAudio = summary.HasAudio,
      FrameDurationMilliseconds = summary.FrameDurationMilliseconds,
      BigEndian = IsBigEndian(data.Span),
    };
  }

  private readonly record struct Summary(
    EaVideoCodecKind VideoCodec, int Width, int Height, int FrameRate, int VideoFrameCount, bool HasAudio,
    int FrameDurationMilliseconds = 0);

  private static Summary _Summarise(ReadOnlyMemory<byte> data) {
    var codec = EaVideoCodecKind.None;
    var width = 0;
    var height = 0;
    var frameRate = 0;
    var frames = 0;
    var hasAudio = false;
    var duration = 0;

    foreach (var chunk in _WalkChunks(data)) {
      var payload = data.Span.Slice(chunk.PayloadOffset, chunk.PayloadLength);

      if (EaChunkType.IsCmv(chunk.FourCc)) {
        if (codec == EaVideoCodecKind.None)
          codec = EaVideoCodecKind.Cmv;
        if (chunk.FourCc == EaChunkType.MVIh && payload.Length >= _CMV_HEADER_MIN_LENGTH) {
          var (w, h, rate) = _ReadCmvHeader(payload);
          if (width == 0) {
            width = w;
            height = h;
            frameRate = rate;
          }
        } else if (chunk.FourCc == EaChunkType.MVIf)
          ++frames;
      } else if (EaChunkType.IsTgv(chunk.FourCc)) {
        if (codec == EaVideoCodecKind.None)
          codec = EaVideoCodecKind.Tgv;
        if (chunk.FourCc == EaChunkType.kVGT && payload.Length >= _TGV_HEADER_MIN_LENGTH && width == 0) {
          var (w, h) = _ReadTgvSize(payload);
          width = w;
          height = h;
        }
        ++frames;
      } else if (EaChunkType.IsMad(chunk.FourCc)) {
        if (codec == EaVideoCodecKind.None)
          codec = EaVideoCodecKind.Mad;
        if (width == 0 && payload.Length >= 12) {
          duration = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
          width = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]);
          height = BinaryPrimitives.ReadUInt16LittleEndian(payload[10..]);
        }
        ++frames;
      } else if (EaChunkType.IsTgq(chunk.FourCc)) {
        if (codec == EaVideoCodecKind.None)
          codec = EaVideoCodecKind.Tgq;
        if (width == 0 && payload.Length >= 4) {
          // TGQ states its own size in the byte order of the chunk sizes.
          var big = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(chunk.ChunkStart + 4)..]) > 0x000FFFFF;
          width = big ? BinaryPrimitives.ReadUInt16BigEndian(payload) : BinaryPrimitives.ReadUInt16LittleEndian(payload);
          height = big ? BinaryPrimitives.ReadUInt16BigEndian(payload[2..]) : BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
        }
        ++frames;
      } else if (chunk.FourCc == EaChunkType.pIQT) {
        if (codec == EaVideoCodecKind.None)
          codec = EaVideoCodecKind.Tqi;
        if (width == 0 && payload.Length >= 4) {
          width = BinaryPrimitives.ReadUInt16LittleEndian(payload);
          height = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
        }
        ++frames;
      } else if (EaChunkType.IsAudio(chunk.FourCc))
        hasAudio = true;
    }

    return new(codec, width, height, frameRate, frames, hasAudio, duration);
  }

  private static (int Width, int Height, int FrameRate) _ReadCmvHeader(ReadOnlySpan<byte> payload)
    => (
      BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]),
      BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]),
      BinaryPrimitives.ReadUInt16LittleEndian(payload[10..]));

  private static (int Width, int Height) _ReadTgvSize(ReadOnlySpan<byte> payload)
    => (BinaryPrimitives.ReadUInt16LittleEndian(payload), BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]));

  private static IEnumerable<ChunkHeader> _WalkChunks(ReadOnlyMemory<byte> data) {
    var at = 0;
    var bigEndian = IsBigEndian(data.Span);
    var skipping = false;
    while (at + _CHUNK_HEADER_LENGTH <= data.Length) {
      var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[at..]);
      var size = bigEndian
        ? BinaryPrimitives.ReadUInt32BigEndian(data.Span[(at + 4)..])
        : BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(at + 4)..]);
      if (size < _CHUNK_HEADER_LENGTH)
        throw new InvalidDataException($"A chunk at byte {at} states a size of {size}, shorter than its header.");

      var payloadOffset = at + _CHUNK_HEADER_LENGTH;
      var payloadLength = checked((int)size - _CHUNK_HEADER_LENGTH);
      if (payloadOffset + (long)payloadLength > data.Length)
        yield break;

      // After a sound stream's end chunk FFmpeg's demuxer skips everything up to the next header chunk;
      // what lies between is not part of the film it plays, and is not part of the one read here.
      if (skipping) {
        if (fourCc is not (EaChunkType._1SNh or EaChunkType.SCHl or EaChunkType.SEAD or EaChunkType.SHEN)) {
          at = payloadOffset + payloadLength;
          continue;
        }
        skipping = false;
      }

      yield return new(fourCc, payloadLength, payloadOffset);
      if (fourCc is EaChunkType._1SNe or EaChunkType.SCEl or EaChunkType.SEND or EaChunkType.SEEN)
        skipping = true;
      at = payloadOffset + payloadLength;
    }
  }

  /// <summary>
  /// Complete video-state, video-frame and audio-family chunks are exposed with their eight-byte
  /// headers intact so a writer can replay every recognised structural record without understanding
  /// nested codec state.
  /// </summary>
  internal static IEnumerable<CodedPacket> ReadPackets(EaContainer container) {
    var data = container.Data;
    long frameIndex = 0;
    var hasVideo = container.VideoCodec != EaVideoCodecKind.None;
    var audioStream = hasVideo ? 1 : 0;

    foreach (var chunk in _WalkChunks(data)) {
      if (EaChunkType.IsCmv(chunk.FourCc)) {
        if (!hasVideo)
          continue;
        if (chunk.FourCc is EaChunkType.MVIh or EaChunkType.MVIe)
          yield return new(StreamIndex: 0, Data: _WithHeader(data, chunk));
        else if (chunk.FourCc == EaChunkType.MVIf) {
          var isIntra = chunk.PayloadLength >= 2
            && BinaryPrimitives.ReadUInt16LittleEndian(data.Span.Slice(chunk.PayloadOffset, 2)) == 0;
          yield return new(
            StreamIndex: 0,
            Data: _WithHeader(data, chunk),
            PresentationTimestamp: frameIndex,
            DecodeTimestamp: frameIndex,
            Duration: 1,
            IsKeyFrame: isIntra);
          ++frameIndex;
        }
      } else if (EaChunkType.IsTgv(chunk.FourCc)) {
        if (!hasVideo)
          continue;
        yield return new(
          StreamIndex: 0,
          Data: _WithHeader(data, chunk),
          PresentationTimestamp: frameIndex,
          DecodeTimestamp: frameIndex,
          Duration: 1,
          IsKeyFrame: chunk.FourCc == EaChunkType.kVGT);
        ++frameIndex;
      } else if (EaChunkType.IsMad(chunk.FourCc) || EaChunkType.IsTgq(chunk.FourCc) || chunk.FourCc == EaChunkType.pIQT) {
        if (!hasVideo)
          continue;
        // FFmpeg hands MAD and TGQ pictures over with their chunk header and TQI's without, and each
        // decoder reads its packet accordingly.
        yield return new(
          StreamIndex: 0,
          Data: chunk.FourCc == EaChunkType.pIQT ? data.Slice(chunk.PayloadOffset, chunk.PayloadLength) : _WithHeader(data, chunk),
          PresentationTimestamp: frameIndex,
          DecodeTimestamp: frameIndex,
          Duration: 1,
          IsKeyFrame: chunk.FourCc is not (EaChunkType.MADm or EaChunkType.MADe));
        ++frameIndex;
      } else if (EaChunkType.IsAudio(chunk.FourCc) && container.HasAudio)
        yield return new(StreamIndex: audioStream, Data: _WithHeader(data, chunk), IsKeyFrame: true);
    }
  }

  private static ReadOnlyMemory<byte> _WithHeader(ReadOnlyMemory<byte> data, ChunkHeader chunk)
    => data.Slice(chunk.ChunkStart, chunk.ChunkLength);
}
