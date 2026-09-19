using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vqa;

/// <summary>
/// Splits a Westwood VQA file into its IFF-style chunks and hands coded pictures to the video codec.
/// </summary>
/// <remarks>
/// A normal picture is a <c>VQFR</c> containing codec sub-chunks. HiColor VQA adds one wrinkle at the
/// container boundary: after the first picture, a replacement full codebook may live in a top-level
/// <c>VQFL</c> immediately before the <c>VQFR</c> that starts using it. A coded packet therefore joins
/// pending VQFL payloads with that following VQFR payload. It still does not interpret a vector or a
/// pointer command; it merely preserves all bytes belonging to one decoder input picture.
/// </remarks>
internal static class VqaReader {

  private static readonly byte[] _Signature = "FORM"u8.ToArray();
  private static readonly byte[] _FormType = "WVQA"u8.ToArray();
  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _FORM_PREFIX_LENGTH = 12;
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
      if (!chunk.Id.Span.SequenceEqual("VQHD"u8))
        continue;

      if (chunk.Length < _HEADER_PAYLOAD_LENGTH)
        throw new InvalidDataException($"A VQHD chunk is {chunk.Length} bytes, short of the forty-two a VQA header needs.");

      headerPayload = data.Slice(chunk.PayloadOffset, _HEADER_PAYLOAD_LENGTH);
      haveHeader = true;
      break;
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

  private static IEnumerable<ChunkHeader> _WalkChunks(ReadOnlyMemory<byte> data) {
    var at = _FORM_PREFIX_LENGTH;
    var length = data.Length;

    while (at < length) {
      if (at + _CHUNK_HEADER_LENGTH > length)
        throw new InvalidDataException($"A chunk header would start at byte {at}, {length - at} bytes from the end of a file whose chunk headers are eight bytes each.");

      var id = data.Slice(at, 4);
      var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.Span[(at + 4)..]));
      var payloadOffset = at + _CHUNK_HEADER_LENGTH;

      if (payloadOffset + size > length)
        yield break;

      yield return new(id, payloadOffset, size);
      at = payloadOffset + size + (size & 1);
    }
  }

  internal static IEnumerable<CodedPacket> ReadPackets(VqaContainer container) {
    var data = container.Data;
    var hasAudio = container.AudioSampleRate > 0 && container.AudioChannels > 0;
    var pendingVideoPrefix = new List<ReadOnlyMemory<byte>>();

    long videoFrame = 0;
    long audioSample = 0;

    foreach (var chunk in _WalkChunks(data)) {
      if (chunk.Id.Span.SequenceEqual("VQFL"u8)) {
        pendingVideoPrefix.Add(data.Slice(chunk.PayloadOffset, chunk.Length));
        continue;
      }

      if (chunk.Id.Span.SequenceEqual("VQFR"u8)) {
        var framePayload = data.Slice(chunk.PayloadOffset, chunk.Length);
        ReadOnlyMemory<byte> packetData;
        if (pendingVideoPrefix.Count == 0) {
          packetData = framePayload;
        } else {
          var length = framePayload.Length;
          foreach (var prefix in pendingVideoPrefix)
            length = checked(length + prefix.Length);

          var joined = new byte[length];
          var at = 0;
          foreach (var prefix in pendingVideoPrefix) {
            prefix.Span.CopyTo(joined.AsSpan(at));
            at += prefix.Length;
          }
          framePayload.Span.CopyTo(joined.AsSpan(at));
          packetData = joined;
          pendingVideoPrefix.Clear();
        }

        yield return new(
          StreamIndex: 0,
          Data: packetData,
          PresentationTimestamp: videoFrame,
          DecodeTimestamp: videoFrame,
          Duration: 1,
          // VQA keeps palette/codebook state even in the old intra-picture form and HiColor also keeps
          // the previous framebuffer. Without parsing codec commands the only universally safe seek
          // point is the stream's beginning.
          IsKeyFrame: videoFrame == 0);
        ++videoFrame;
        continue;
      }

      if (hasAudio && chunk.Id.Span[..3].SequenceEqual("SND"u8)) {
        var sampleCount = chunk.Length / 2;
        yield return new(
          StreamIndex: 1,
          Data: data.Slice(chunk.PayloadOffset, chunk.Length),
          PresentationTimestamp: audioSample,
          IsKeyFrame: true);
        audioSample += sampleCount;
      }
    }

    if (pendingVideoPrefix.Count != 0)
      throw new InvalidDataException("A VQA file ends after a VQFL codebook chunk without the VQFR picture that should follow it.");
  }
}
