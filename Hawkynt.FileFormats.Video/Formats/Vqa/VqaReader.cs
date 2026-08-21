using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vqa;

/// <summary>
/// Splits a Westwood VQA file into its RIFF-style chunks and hands out the ones a caller can do
/// anything with as packets, without reading a single codebook entry or a single index byte.
/// </summary>
/// <remarks>
/// Published in Gordan Ugarkovic's VQA format description (mirrored at
/// <c>multimedia.cx/vqa_overview.htm</c>): a file opens with a <c>FORM</c> chunk naming its type
/// <c>WVQA</c>, and every chunk after that — including <c>FORM</c> itself — is a four-character ID and
/// a four-byte big-endian size, RIFF's own layout except that the size is big-endian where RIFF's own
/// chunks are little-endian. <c>FORM</c>'s own stated size is not trustworthy: measured against real
/// files, one names a size covering only its header chunks and stops there while the real file runs on
/// for megabytes past it, so this walks chunks by their own sizes to the end of the file rather than to
/// where <c>FORM</c> says it ends.
/// <para/>
/// A picture is not one chunk. <c>VQHD</c> states the picture size, the block size a codebook's entries
/// are measured in, and the format version once, near the start of the file; then <c>VQFR</c> chunks —
/// one a picture — each wrap a handful of sub-chunks: a full or partial codebook, sometimes a palette,
/// and always an index table naming which codebook entry (or which solid colour) paints each block.
/// What a demuxer can say without decoding any of that is which stream a chunk belongs to and where one
/// picture's worth of it starts and stops — the rest is <see cref="Codecs.VqaVideoDecoder"/>'s.
/// </remarks>
internal static class VqaReader {

  private static readonly byte[] _Signature = "FORM"u8.ToArray();
  private static readonly byte[] _FormType = "WVQA"u8.ToArray();
  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _FORM_PREFIX_LENGTH = 12; // "FORM" + big-endian size + "WVQA"
  private const int _HEADER_PAYLOAD_LENGTH = 42;

  internal readonly record struct ChunkHeader(ReadOnlyMemory<byte> Id, int PayloadOffset, int Length);

  internal readonly record struct Summary(
    int Width, int Height, int BlockWidth, int BlockHeight, int VideoFrameCount,
    int AudioSampleRate, int AudioChannels, ReadOnlyMemory<byte> HeaderPayload);

  internal static VqaContainer Open(ReadOnlyMemory<byte> data) {
    if (data.Length < _FORM_PREFIX_LENGTH || !data.Span[..4].SequenceEqual(_Signature) || !data.Span.Slice(8, 4).SequenceEqual(_FormType))
      throw new NotSupportedException(
        "The file does not open with a \"FORM\" chunk naming its type \"WVQA\". This is not a Westwood VQA file.");

    var summary = _Summarise(data);
    return new() {
      Data = data,
      Width = summary.Width,
      Height = summary.Height,
      BlockWidth = summary.BlockWidth,
      BlockHeight = summary.BlockHeight,
      VideoFrameCount = summary.VideoFrameCount,
      AudioSampleRate = summary.AudioSampleRate,
      AudioChannels = summary.AudioChannels,
      HeaderPayload = summary.HeaderPayload,
    };
  }

  private static Summary _Summarise(ReadOnlyMemory<byte> data) {
    ReadOnlyMemory<byte> headerPayload = default;
    var haveHeader = false;

    foreach (var chunk in _WalkChunks(data)) {
      if (chunk.Id.Span.SequenceEqual("VQHD"u8)) {
        if (chunk.Length < _HEADER_PAYLOAD_LENGTH)
          throw new InvalidDataException($"A VQHD chunk is {chunk.Length} bytes, short of the forty-two a VQA header needs.");

        headerPayload = data.Slice(chunk.PayloadOffset, _HEADER_PAYLOAD_LENGTH);
        haveHeader = true;
        break; // VQHD is always the first chunk after FORM's own prefix; nothing else needs walking here.
      }
    }

    if (!haveHeader)
      throw new InvalidDataException("No VQHD chunk was found anywhere in the file. Every picture's size comes from that chunk and nowhere else.");

    var payload = headerPayload.Span;
    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]);
    var blockWidth = payload[10];
    var blockHeight = payload[11];
    var frameCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
    var audioSampleRate = BinaryPrimitives.ReadUInt16LittleEndian(payload[24..]);
    var audioChannels = payload[26];

    if (width == 0 || height == 0 || blockWidth == 0 || blockHeight == 0)
      throw new InvalidDataException($"A VQHD chunk states a picture of {width}x{height} in {blockWidth}x{blockHeight} blocks, which has no pixels or no blocks.");

    return new(width, height, blockWidth, blockHeight, frameCount, audioSampleRate, audioChannels, headerPayload);
  }

  /// <summary>Walks every top-level chunk from right after <c>FORM</c>'s twelve-byte prefix to the end
  /// of the file, trusting each chunk's own size and not <c>FORM</c>'s.</summary>
  private static IEnumerable<ChunkHeader> _WalkChunks(ReadOnlyMemory<byte> data) {
    var at = _FORM_PREFIX_LENGTH;
    var length = data.Length;

    while (at < length) {
      if (at + _CHUNK_HEADER_LENGTH > length)
        throw new InvalidDataException($"A chunk header would start at byte {at}, {length - at} bytes from the end of a file whose chunk headers are eight bytes each.");

      var id = data.Slice(at, 4);
      var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Span[(at + 4)..]);
      var payloadOffset = at + _CHUNK_HEADER_LENGTH;

      if (payloadOffset + size > length)
        // A chunk that runs past the end of the file is where a real recording is free to simply stop —
        // the same shape RoQ's and id Cinematic's own truncated samples take — so this reader ends the
        // walk here rather than refusing the file outright.
        yield break;

      yield return new(id, payloadOffset, size);

      var padding = size & 1; // chunks pad to an even length, RIFF-style
      at = payloadOffset + size + padding;
    }
  }

  /// <summary>Walks the film's chunks a second time, handing out the ones a caller can do anything
  /// with as packets — pictures on stream 0, sound on stream 1.</summary>
  internal static IEnumerable<CodedPacket> ReadPackets(VqaContainer container) {
    var data = container.Data;
    var hasAudio = container.AudioSampleRate > 0 && container.AudioChannels > 0;

    long videoFrame = 0;
    long audioSample = 0;

    foreach (var chunk in _WalkChunks(data)) {
      if (chunk.Id.Span.SequenceEqual("VQFR"u8)) {
        yield return new(
          StreamIndex: 0,
          Data: data.Slice(chunk.PayloadOffset, chunk.Length),
          PresentationTimestamp: videoFrame,
          DecodeTimestamp: videoFrame,
          Duration: 1,
          IsKeyFrame: true);
        ++videoFrame;
      } else if (hasAudio && chunk.Id.Span[..3].SequenceEqual("SND"u8)) {
        var sampleCount = chunk.Length / 2; // this project decodes no VQA audio codec, so only the
                                             // sixteen-bit-sample count the format's own header states
                                             // throughout is used to place packets on the timeline.
        yield return new(
          StreamIndex: 1,
          Data: data.Slice(chunk.PayloadOffset, chunk.Length),
          PresentationTimestamp: audioSample,
          IsKeyFrame: true);
        audioSample += sampleCount;
      }
      // VQHD, FINF and any other top-level chunk this reader does not recognise are skipped — the
      // RIFF-style layout is built exactly so a reader can do that without knowing what it skipped.
    }
  }
}
