using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Anim;

/// <summary>
/// Splits an IFF <c>FORM ANIM</c> into its <c>FORM ILBM</c> frame packets while retaining ANHD timing
/// and enough frame semantics to mark independently decodable operation-0 pictures.
/// </summary>
internal static class AnimChunkReader {

  private static bool _Is(ReadOnlySpan<byte> data, int offset, string tag) {
    if (offset < 0 || offset + 4 > data.Length)
      return false;
    return data[offset] == tag[0] && data[offset + 1] == tag[1] && data[offset + 2] == tag[2] && data[offset + 3] == tag[3];
  }

  internal static bool LooksPlausible(ReadOnlySpan<byte> header)
    => header.Length >= 12 && _Is(header, 0, "FORM") && _Is(header, 8, "ANIM");

  internal readonly record struct RawFrame(int Offset, int Length);
  private readonly record struct FrameHeader(uint RelativeTime, bool IsKeyFrame);

  private static IEnumerable<RawFrame> _WalkFrames(ReadOnlyMemory<byte> data) {
    if (data.Length < 12 || !_Is(data.Span, 0, "FORM") || !_Is(data.Span, 8, "ANIM"))
      yield break;

    var formSize = BinaryPrimitives.ReadUInt32BigEndian(data.Span[4..]);
    var end = (int)Math.Min((long)8 + formSize, data.Length);
    var pos = 12;
    while (pos + 8 <= end) {
      var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(data.Span[(pos + 4)..]);
      var totalLength64 = 8UL + chunkSize;
      if (totalLength64 > int.MaxValue || (ulong)pos + totalLength64 > (ulong)data.Length)
        yield break;
      var totalLength = (int)totalLength64;
      if (_Is(data.Span, pos, "FORM") && pos + 12 <= data.Length && _Is(data.Span, pos + 8, "ILBM"))
        yield return new(pos, totalLength);
      var advance = totalLength64 + (chunkSize & 1);
      if (advance > int.MaxValue || pos > int.MaxValue - (int)advance)
        yield break;
      pos += (int)advance;
    }
  }

  internal static AnimContainer Open(ReadOnlyMemory<byte> data) {
    if (data.Length < 12)
      throw new NotSupportedException(
        $"The file is {data.Length} bytes, short of the twelve bytes an IFF group chunk header needs. This is not an IFF ANIM file.");
    if (!LooksPlausible(data.Span))
      throw new NotSupportedException("This file does not open with 'FORM', a size, and 'ANIM'. This is not an IFF ANIM file.");

    var frames = new List<RawFrame>();
    foreach (var frame in _WalkFrames(data))
      frames.Add(frame);
    var (width, height) = frames.Count > 0 ? _ReadDimensions(data.Span, frames[0]) : (0, 0);
    return new() { Data = data, Width = width, Height = height, FrameCount = frames.Count };
  }

  private static (int Width, int Height) _ReadDimensions(ReadOnlySpan<byte> data, RawFrame first) {
    var pos = first.Offset + 12;
    var end = first.Offset + first.Length;
    while (pos + 8 <= end) {
      var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(data[(pos + 4)..]);
      if (chunkSize > int.MaxValue || pos + 8L + chunkSize > end)
        break;
      if (_Is(data, pos, "BMHD") && chunkSize >= 4) {
        var width = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 8)..]);
        var height = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 10)..]);
        return (width, height);
      }
      pos += checked(8 + (int)chunkSize + (int)(chunkSize & 1));
    }
    return (0, 0);
  }

  private static FrameHeader _ReadFrameHeader(ReadOnlySpan<byte> data, RawFrame frame) {
    var pos = frame.Offset + 12;
    var end = frame.Offset + frame.Length;
    byte? operation = null;
    uint relativeTime = 0;
    var hasBody = false;

    while (pos + 8 <= end) {
      var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(data[(pos + 4)..]);
      if (chunkSize > int.MaxValue || pos + 8L + chunkSize > end)
        break;
      var size = (int)chunkSize;
      if (_Is(data, pos, "ANHD") && size >= 18) {
        operation = data[pos + 8];
        relativeTime = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos + 8 + 14, 4));
      } else if (_Is(data, pos, "BODY"))
        hasBody = true;
      pos += checked(8 + size + (size & 1));
    }

    return new(relativeTime, hasBody && operation is null or 0);
  }

  private static long _Delay(uint relativeTime) => relativeTime == 0 ? 1 : relativeTime;

  internal static IEnumerable<CodedPacket> ReadPackets(AnimContainer container) {
    var frames = new List<(RawFrame Frame, FrameHeader Header)>();
    foreach (var raw in _WalkFrames(container.Data))
      frames.Add((raw, _ReadFrameHeader(container.Data.Span, raw)));

    long timestamp = 0;
    for (var i = 0; i < frames.Count; ++i) {
      if (i > 0)
        timestamp = checked(timestamp + _Delay(frames[i].Header.RelativeTime));
      var duration = i + 1 < frames.Count
        ? _Delay(frames[i + 1].Header.RelativeTime)
        : _Delay(frames[i].Header.RelativeTime);
      var raw = frames[i].Frame;
      yield return new(
        StreamIndex: 0,
        Data: container.Data.Slice(raw.Offset, raw.Length),
        PresentationTimestamp: timestamp,
        DecodeTimestamp: timestamp,
        Duration: duration,
        IsKeyFrame: frames[i].Header.IsKeyFrame);
    }
  }
}
