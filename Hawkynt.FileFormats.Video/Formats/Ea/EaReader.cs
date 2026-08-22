using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ea;

/// <summary>
/// Splits an Electronic Arts multimedia file into the chunks it is built from, without reading a
/// single coded pixel.
/// </summary>
/// <remarks>
/// Every chunk is eight bytes of its own header — a four-character FourCC and a little-endian size
/// that counts those eight bytes as well as the payload — followed by that many bytes of payload. A
/// chunk this reader has no name for costs nothing to step over: its own size says where the next one
/// starts, and nothing about walking the file needs to know what the chunk held.
/// <para/>
/// <b>The declared size includes its own eight-byte header, confirmed against two real files rather
/// than assumed.</b> <c>TITLE.CMV</c>'s first chunk states <c>0x00000258</c> (600) and the next chunk
/// this reader can independently locate — by searching for the next <c>MVIf</c> signature rather than
/// trusting the size field being checked — sits at exactly file offset 600; the same check on the
/// second chunk (a picture stating <c>0x00009C4C</c>, 40012) lands the next chunk at exactly 40612,
/// which is also ffprobe's own reported byte offset for that picture's packet.
/// </remarks>
internal static class EaReader {

  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _CMV_HEADER_MIN_LENGTH = 0x10;
  private const int _TGV_HEADER_MIN_LENGTH = 0x0C;

  internal readonly record struct ChunkHeader(uint FourCc, int PayloadLength, int PayloadOffset) {
    internal int ChunkStart => this.PayloadOffset - _CHUNK_HEADER_LENGTH;
    internal int ChunkLength => this.PayloadLength + _CHUNK_HEADER_LENGTH;
  }

  /// <summary>
  /// A signature this format has none of, stood in for by a plausibility check: the first four bytes
  /// name a chunk kind this reader is built from, and the size behind them is at least the eight-byte
  /// header those bytes are themselves part of.
  /// </summary>
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
    => EaChunkType.IsCmv(fourCc) || EaChunkType.IsTgv(fourCc) || fourCc is EaChunkType.SCHl or EaChunkType.SEAD;

  internal static EaContainer Open(ReadOnlyMemory<byte> data) {
    if (!LooksPlausible(data.Span))
      throw new NotSupportedException(
        "The file does not open with a recognisable Electronic Arts chunk (MVIh, kVGT or an audio "
        + "stream header) stating a plausible size. This is not an Electronic Arts multimedia file.");

    var summary = _Summarise(data);
    return new() {
      Data = data,
      VideoCodec = summary.VideoCodec,
      Width = summary.Width,
      Height = summary.Height,
      FrameRate = summary.FrameRate,
      VideoFrameCount = summary.VideoFrameCount,
    };
  }

  private readonly record struct Summary(EaVideoCodecKind VideoCodec, int Width, int Height, int FrameRate, int VideoFrameCount);

  private static Summary _Summarise(ReadOnlyMemory<byte> data) {
    var codec = EaVideoCodecKind.None;
    var width = 0;
    var height = 0;
    var frameRate = 0;
    var frames = 0;

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
        } else if (chunk.FourCc == EaChunkType.MVIf) {
          ++frames;
        }
      } else if (EaChunkType.IsTgv(chunk.FourCc)) {
        if (codec == EaVideoCodecKind.None)
          codec = EaVideoCodecKind.Tgv;

        if (chunk.FourCc == EaChunkType.kVGT && payload.Length >= _TGV_HEADER_MIN_LENGTH && width == 0) {
          var (w, h) = _ReadTgvSize(payload);
          width = w;
          height = h;
        }

        ++frames;
      }
    }

    return new(codec, width, height, frameRate, frames);
  }

  /// <summary>Reads width, height and frame rate from an <c>MVIh</c> chunk's payload — offsets 0x04,
  /// 0x06 and 0x0A, all confirmed against <c>TITLE.CMV</c>: 200, 200 and 10 there, matching ffprobe's
  /// own reported picture size and frame rate exactly.</summary>
  private static (int Width, int Height, int FrameRate) _ReadCmvHeader(ReadOnlySpan<byte> payload) {
    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
    var frameRate = BinaryPrimitives.ReadUInt16LittleEndian(payload[10..]);
    return (width, height, frameRate);
  }

  /// <summary>Reads width and height from a <c>kVGT</c> chunk's payload — offsets 0x00 and 0x02,
  /// confirmed against a real 320x200 sample the same way the CMV header is.</summary>
  private static (int Width, int Height) _ReadTgvSize(ReadOnlySpan<byte> payload) {
    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
    return (width, height);
  }

  /// <summary>
  /// Walks the chunk run once, stopping cleanly — rather than refusing the file outright — whenever
  /// what remains is too short to be another whole chunk header or the last chunk's own stated size
  /// would run past the end of the file. A chunk whose stated size is smaller than the eight-byte
  /// header it is itself measured from is not a truncation and is refused instead.
  /// </summary>
  private static IEnumerable<ChunkHeader> _WalkChunks(ReadOnlyMemory<byte> data) {
    var at = 0;
    var length = data.Length;

    while (at + _CHUNK_HEADER_LENGTH <= length) {
      var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[at..]);
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(at + 4)..]);

      if (size < _CHUNK_HEADER_LENGTH)
        throw new InvalidDataException(
          $"A chunk at byte {at} states a size of {size} bytes, short of the eight-byte header that "
          + "size is supposed to include.");

      var payloadOffset = at + _CHUNK_HEADER_LENGTH;
      var payloadLength = (int)size - _CHUNK_HEADER_LENGTH;

      if (payloadOffset + payloadLength > length)
        yield break; // the file ends part way through its last chunk — read as far as it goes.

      yield return new(fourCc, payloadLength, payloadOffset);
      at = payloadOffset + payloadLength;
    }
  }

  /// <summary>Walks the file's chunks a second time, handing out the ones a caller can do anything
  /// with as packets: an <c>MVIh</c>/<c>MVIf</c> pair for CMV, a <c>kVGT</c>/<c>fVGT</c> pair for TGV.
  /// Every other chunk — every audio chunk, every video codec this reader has no decoder for — is
  /// walked past and produces nothing, exactly as an unrecognised RIFF or RealMedia chunk is.</summary>
  internal static IEnumerable<CodedPacket> ReadPackets(EaContainer container) {
    var data = container.Data;
    long frameIndex = 0;

    foreach (var chunk in _WalkChunks(data)) {
      if (EaChunkType.IsCmv(chunk.FourCc)) {
        if (chunk.FourCc == EaChunkType.MVIh) {
          yield return new(StreamIndex: 0, Data: _WithHeader(data, chunk));
        } else if (chunk.FourCc == EaChunkType.MVIf) {
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
        // MVIe carries no payload of its own and needs no handling beyond being skipped: the next
        // MVIh, if any, restates the palette from scratch and nothing here treats it as an error.
      } else if (EaChunkType.IsTgv(chunk.FourCc)) {
        yield return new(
          StreamIndex: 0,
          Data: _WithHeader(data, chunk),
          PresentationTimestamp: frameIndex,
          DecodeTimestamp: frameIndex,
          Duration: 1,
          IsKeyFrame: chunk.FourCc == EaChunkType.kVGT);
        ++frameIndex;
      }
    }
  }

  /// <summary>The chunk's own eight-byte header, kept in front of the payload — the same reasoning as
  /// RoQ's and Interplay MVE's: a packet carries enough for the codec to tell which chunk it is
  /// without the container saying so twice.</summary>
  private static ReadOnlyMemory<byte> _WithHeader(ReadOnlyMemory<byte> data, ChunkHeader chunk)
    => data.Slice(chunk.ChunkStart, chunk.ChunkLength);
}
