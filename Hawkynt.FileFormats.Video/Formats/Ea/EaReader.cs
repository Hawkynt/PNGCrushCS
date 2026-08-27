using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ea;

/// <summary>
/// Splits an Electronic Arts multimedia file into its self-delimiting chunks without decoding a
/// single coded pixel or parsing any of EA's nested audio-codec patch headers.
/// </summary>
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
    return size >= _CHUNK_HEADER_LENGTH;
  }

  private static bool _IsKnownChunk(uint fourCc)
    => EaChunkType.IsCmv(fourCc) || EaChunkType.IsTgv(fourCc) || EaChunkType.IsAudio(fourCc);

  internal static EaContainer Open(ReadOnlyMemory<byte> data) {
    if (!LooksPlausible(data.Span))
      throw new NotSupportedException(
        "The file does not open with a recognisable Electronic Arts video or audio chunk stating a "
        + "plausible size. This is not an Electronic Arts multimedia file this reader recognises.");

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

  private static (int Width, int Height, int FrameRate) _ReadCmvHeader(ReadOnlySpan<byte> payload) {
    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
    var frameRate = BinaryPrimitives.ReadUInt16LittleEndian(payload[10..]);
    return (width, height, frameRate);
  }

  private static (int Width, int Height) _ReadTgvSize(ReadOnlySpan<byte> payload) {
    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
    return (width, height);
  }

  private static IEnumerable<ChunkHeader> _WalkChunks(ReadOnlyMemory<byte> data) {
    var at = 0;
    var length = data.Length;

    while (at + _CHUNK_HEADER_LENGTH <= length) {
      var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[at..]);
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(at + 4)..]);

      if (size < _CHUNK_HEADER_LENGTH)
        throw new InvalidDataException(
          $"A chunk at byte {at} states a size of {size} bytes, short of the eight-byte header that size is supposed to include.");

      var payloadOffset = at + _CHUNK_HEADER_LENGTH;
      var payloadLength = checked((int)size - _CHUNK_HEADER_LENGTH);
      if (payloadOffset + (long)payloadLength > length)
        yield break;

      yield return new(fourCc, payloadLength, payloadOffset);
      at = payloadOffset + payloadLength;
    }
  }

  /// <summary>
  /// Walks the chunks again. EA video codec/state chunks remain on the video stream; complete audio
  /// family chunks remain on a separate audio stream. Keeping the eight-byte chunk header on both is
  /// intentional: it lets a decoder or remuxer distinguish header, data, loop and end records without
  /// this generic EA container parsing the nested codec-specific protocol.
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
        if (chunk.FourCc == EaChunkType.MVIh)
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
