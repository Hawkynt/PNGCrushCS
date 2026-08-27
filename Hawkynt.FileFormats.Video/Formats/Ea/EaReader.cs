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
    return BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) >= _CHUNK_HEADER_LENGTH;
  }

  private static bool _IsKnownChunk(uint fourCc)
    => EaChunkType.IsCmv(fourCc) || EaChunkType.IsTgv(fourCc) || EaChunkType.IsAudio(fourCc);

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
    };
  }

  private readonly record struct Summary(
    EaVideoCodecKind VideoCodec, int Width, int Height, int FrameRate, int VideoFrameCount, bool HasAudio);

  private static Summary _Summarise(ReadOnlyMemory<byte> data) {
    var codec = EaVideoCodecKind.None;
    var width = 0;
    var height = 0;
    var frameRate = 0;
    var frames = 0;
    var hasAudio = false;

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
      } else if (EaChunkType.IsAudio(chunk.FourCc))
        hasAudio = true;
    }

    return new(codec, width, height, frameRate, frames, hasAudio);
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
    while (at + _CHUNK_HEADER_LENGTH <= data.Length) {
      var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[at..]);
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(at + 4)..]);
      if (size < _CHUNK_HEADER_LENGTH)
        throw new InvalidDataException($"A chunk at byte {at} states a size of {size}, shorter than its header.");

      var payloadOffset = at + _CHUNK_HEADER_LENGTH;
      var payloadLength = checked((int)size - _CHUNK_HEADER_LENGTH);
      if (payloadOffset + (long)payloadLength > data.Length)
        yield break;
      yield return new(fourCc, payloadLength, payloadOffset);
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
      } else if (EaChunkType.IsAudio(chunk.FourCc) && container.HasAudio)
        yield return new(StreamIndex: audioStream, Data: _WithHeader(data, chunk), IsKeyFrame: true);
    }
  }

  private static ReadOnlyMemory<byte> _WithHeader(ReadOnlyMemory<byte> data, ChunkHeader chunk)
    => data.Slice(chunk.ChunkStart, chunk.ChunkLength);
}
