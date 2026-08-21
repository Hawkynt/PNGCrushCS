using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RoqVideo;

/// <summary>
/// Splits a RoQ file into the chunks it is a flat run of, without reading a single opcode inside a
/// <c>QUAD_VQ</c> chunk or a single delta inside a sound chunk.
/// </summary>
/// <remarks>
/// RoQ has no separate header-and-index layer the way an AVI or a Matroska has one: after the eight
/// fixed bytes every file opens with, the whole file is one flat sequence of self-delimiting chunks —
/// id, a payload length that is the chunk's own and not the container's, an argument, and the payload.
/// A picture is not one chunk either. <c>INFO</c> states the picture size once, near wherever the
/// first frame happens to be rather than at a fixed offset; a <c>QUAD_CODEBOOK</c> chunk restates the
/// codebook only when it changes, so long runs of frames carry none at all; and only <c>QUAD_VQ</c>
/// ever produces a picture. Sound is interleaved with all of that at whatever cadence the encoder
/// chose. What a demuxer can say without decoding any of it is which of those five kinds of thing each
/// chunk is and which stream it belongs to — nothing about a quadtree, a codebook entry, or a DPCM
/// delta is read here.
/// <para/>
/// Two chunk types carry nothing worth handing out at all. <c>PACKET</c> is a hint that this is a good
/// moment to read ahead, stated as an empty chunk; <c>HANG</c> is a housekeeping marker with neither a
/// picture nor a sample in it. Both are skipped by their stated length rather than turned into packets
/// nobody would know what to do with — the same treatment RealMedia's reader gives a chunk it does not
/// recognise, extended here to two chunks whose only content is the fact of their own presence.
/// </remarks>
internal static class RoqReader {

  /// <summary>The eight bytes every RoQ file begins with, spelled out as the fixed signature chunk:
  /// id <c>0x1084</c>, little-endian, then <c>0xFFFFFFFF</c>, then <c>0x001E</c>.</summary>
  internal static readonly byte[] Signature = [0x84, 0x10, 0xFF, 0xFF, 0xFF, 0xFF, 0x1E, 0x00];

  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _INFO_PAYLOAD_LENGTH = 8;

  /// <summary>One chunk's header, and where its payload lies in the file.</summary>
  internal readonly record struct ChunkHeader(ushort Id, uint Size, ushort Argument, int PayloadOffset);

  /// <summary>Everything <see cref="Open"/> learns by walking the file once, ahead of any packet being
  /// read: the picture size <c>INFO</c> states, how many pictures the file holds, and whether it
  /// carries sound and of which kind.</summary>
  internal readonly record struct Summary(int Width, int Height, int VideoFrameCount, bool HasAudio, bool AudioIsStereo);

  internal static RoqContainer Open(ReadOnlyMemory<byte> data) {
    if (data.Length < Signature.Length || !data.Span[..Signature.Length].SequenceEqual(Signature))
      throw new NotSupportedException(
        "The file does not open with the eight-byte RoQ signature (chunk 0x1084, length 0xFFFFFFFF, "
        + "argument 0x001E). This is not a RoQ file, or is a chunk-shaped format that only resembles one.");

    var summary = _Summarise(data);
    return new() { Data = data, Width = summary.Width, Height = summary.Height, VideoFrameCount = summary.VideoFrameCount, HasAudio = summary.HasAudio, AudioIsStereo = summary.AudioIsStereo };
  }

  /// <summary>Walks the whole file once to answer what <see cref="RoqContainer"/>'s stream metadata
  /// needs — the picture size and how many chunks of each kind there are — none of which is stated
  /// anywhere but has to be found by counting.</summary>
  private static Summary _Summarise(ReadOnlyMemory<byte> data) {
    var width = 0;
    var height = 0;
    var haveInfo = false;
    var frames = 0;
    var hasAudio = false;
    var audioIsStereo = false;

    foreach (var chunk in _WalkHeaders(data)) {
      switch (chunk.Id) {
        case RoqChunkType.INFO:
          if (!haveInfo) {
            (width, height) = _ReadInfo(data.Span, chunk);
            haveInfo = true;
          }
          break;
        case RoqChunkType.QUAD_VQ:
          ++frames;
          break;
        case RoqChunkType.SOUND_MONO:
          hasAudio = true;
          break;
        case RoqChunkType.SOUND_STEREO:
          hasAudio = true;
          audioIsStereo = true;
          break;
      }
    }

    if (!haveInfo)
      throw new InvalidDataException(
        "No RoQ_INFO chunk (0x1001) was found anywhere in the file. Every picture chunk states its "
        + "dimensions in that chunk and nowhere else, so a file without one cannot be sized at all.");

    return new(width, height, frames, hasAudio, audioIsStereo);
  }

  /// <summary>Reads the picture size out of an <c>INFO</c> chunk's fixed eight-byte payload and
  /// checks the two fields the format's documentation says are always <c>8</c> and <c>4</c>.</summary>
  /// <remarks>
  /// Every sample this was measured against carries exactly <c>8</c> and <c>4</c> there. What those
  /// two fields mean when they are not is not established by anything read for this — plausibly a
  /// different macroblock or sub-block size the quadtree walk below does not implement — so a file
  /// stating anything else is refused rather than decoded against block sizes nothing here verified.
  /// </remarks>
  private static (int Width, int Height) _ReadInfo(ReadOnlySpan<byte> data, ChunkHeader chunk) {
    if (chunk.Size < _INFO_PAYLOAD_LENGTH)
      throw new InvalidDataException(
        $"A RoQ_INFO chunk is {chunk.Size} bytes, short of the eight bytes the chunk holds.");

    var payload = data.Slice(chunk.PayloadOffset, _INFO_PAYLOAD_LENGTH);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
    var macroblock = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
    var subblock = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);

    if (width == 0 || height == 0)
      throw new InvalidDataException($"RoQ_INFO states a picture of {width}x{height}, which has no pixels.");

    if (macroblock != 8 || subblock != 4)
      throw new NotSupportedException(
        $"RoQ_INFO states {macroblock} and {subblock} in the two fields every sample this was built "
        + "against states as 8 and 4. Whatever those fields mean when they differ is not implemented.");

    return (width, height);
  }

  /// <summary>Walks every chunk header in the file, in order, past the signature.</summary>
  private static IEnumerable<ChunkHeader> _WalkHeaders(ReadOnlyMemory<byte> data) {
    var at = Signature.Length;

    while (at < data.Length) {
      if (at + _CHUNK_HEADER_LENGTH > data.Length)
        throw new InvalidDataException(
          $"A RoQ chunk header would start at byte {at}, {data.Length - at} bytes from the end of a "
          + "file whose chunk headers are eight bytes each.");

      // A span cannot be held across a yield, so it is read fresh from the memory each time round
      // rather than cached in a local that would have to live across one.
      var id = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[at..]);
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(at + 2)..]);
      var argument = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(at + 6)..]);
      var payloadOffset = at + _CHUNK_HEADER_LENGTH;

      if (size > int.MaxValue || payloadOffset + (long)size > data.Length)
        throw new InvalidDataException(
          $"A RoQ chunk of type 0x{id:X4} at byte {at} states a payload of {size} bytes, which runs "
          + $"past the file's {data.Length} bytes.");

      yield return new(id, size, argument, payloadOffset);
      at = payloadOffset + (int)size;
    }
  }

  /// <summary>Walks the film's chunks a second time, this time handing each one out as a packet — the
  /// picture-carrying kinds on stream 0 and sound on stream 1, everything else stepped over.</summary>
  internal static IEnumerable<CodedPacket> ReadPackets(RoqContainer container) {
    var data = container.Data;
    var audioStreamIndex = container.HasAudio ? 1 : -1;

    var videoFrame = 0L;
    var isFirstPicture = true;
    long audioSample = 0;

    foreach (var chunk in _WalkHeaders(data)) {
      var payload = data.Slice(chunk.PayloadOffset, (int)chunk.Size);

      switch (chunk.Id) {
        case RoqChunkType.INFO:
        case RoqChunkType.QUAD_CODEBOOK:
        case RoqChunkType.JPEG:
          yield return new(StreamIndex: 0, Data: _WithHeader(data, chunk));
          break;

        case RoqChunkType.QUAD_VQ:
          yield return new(
            StreamIndex: 0,
            Data: _WithHeader(data, chunk),
            PresentationTimestamp: videoFrame,
            DecodeTimestamp: videoFrame,
            Duration: 1,
            IsKeyFrame: isFirstPicture);
          ++videoFrame;
          isFirstPicture = false;
          break;

        case RoqChunkType.SOUND_MONO:
          if (audioStreamIndex >= 0) {
            yield return new(StreamIndex: audioStreamIndex, Data: payload, PresentationTimestamp: audioSample, IsKeyFrame: true);
            audioSample += payload.Length;
          }
          break;

        case RoqChunkType.SOUND_STEREO:
          if (audioStreamIndex >= 0) {
            yield return new(StreamIndex: audioStreamIndex, Data: payload, PresentationTimestamp: audioSample, IsKeyFrame: true);
            audioSample += payload.Length / 2;
          }
          break;

        // HANG and PACKET carry nothing a caller could do anything with; stepped over like a chunk
        // nobody here has heard of.
      }
    }
  }

  /// <summary>The codec's own eight-byte chunk header, kept in front of the payload.</summary>
  /// <remarks>
  /// A video packet is handed to the codec whole, header included, rather than pre-parsed — the same
  /// way a Cinepak or Microsoft Video 1 packet carries its own frame header. That is what lets one
  /// <c>TryDecode</c> tell an <c>INFO</c> restatement from a codebook update from an actual picture by
  /// the two bytes at its own front, without the container having to say which is which a second time.
  /// </remarks>
  private static ReadOnlyMemory<byte> _WithHeader(ReadOnlyMemory<byte> data, ChunkHeader chunk)
    => data.Slice(chunk.PayloadOffset - _CHUNK_HEADER_LENGTH, _CHUNK_HEADER_LENGTH + (int)chunk.Size);
}
