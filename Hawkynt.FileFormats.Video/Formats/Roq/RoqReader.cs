using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RoqVideo;

/// <summary>Parses the flat RoQ chunk stream and exposes video/audio packets without decoding them.</summary>
internal static class RoqReader {
  internal static readonly byte[] Signature = [0x84, 0x10, 0xFF, 0xFF, 0xFF, 0xFF, 0x1E, 0x00];

  private const int _HeaderLength = 8;
  private const int _InfoLength = 8;

  internal readonly record struct ChunkHeader(ushort Id, uint Size, ushort Argument, int PayloadOffset);
  internal readonly record struct Summary(
    int Width,
    int Height,
    int VideoFrameCount,
    bool HasAudio,
    bool AudioIsStereo,
    int FrameRate,
    int MotionScale,
    bool IsExtendedProfile);

  internal static RoqContainer Open(ReadOnlyMemory<byte> data) {
    var (frameRate, motionScale, extended) = _ReadSignature(data.Span);
    var summary = _Summarise(data, frameRate, motionScale, extended);
    return new() {
      Data = data,
      Width = summary.Width,
      Height = summary.Height,
      VideoFrameCount = summary.VideoFrameCount,
      HasAudio = summary.HasAudio,
      AudioIsStereo = summary.AudioIsStereo,
      FrameRate = summary.FrameRate,
      MotionScale = summary.MotionScale,
      IsExtendedProfile = summary.IsExtendedProfile,
    };
  }

  internal static byte[] CreateSignature(int frameRate) {
    if (frameRate is <= 0 or > ushort.MaxValue)
      throw new NotSupportedException($"RoQ stores its frame rate in sixteen bits; {frameRate} cannot be represented.");
    var result = new byte[_HeaderLength];
    BinaryPrimitives.WriteUInt16LittleEndian(result, RoqChunkType.SIGNATURE);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(2), uint.MaxValue);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), (ushort)frameRate);
    return result;
  }

  private static (int FrameRate, int MotionScale, bool Extended) _ReadSignature(ReadOnlySpan<byte> data) {
    if (data.Length < _HeaderLength)
      throw new NotSupportedException("The file is too short for RoQ's eight-byte signature header.");

    var id = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var size = BinaryPrimitives.ReadUInt32LittleEndian(data[2..]);
    var argument = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    if (id != RoqChunkType.SIGNATURE)
      throw new NotSupportedException($"The file starts with chunk 0x{id:X4}, not RoQ's 0x{RoqChunkType.SIGNATURE:X4} signature.");

    if (size == uint.MaxValue) {
      if (argument == 0)
        throw new InvalidDataException("The standard RoQ signature states a zero frame rate.");
      return (argument, 1, false);
    }

    if (size == 0)
      return argument == 0 ? (30, 2, true) : (argument, 1, true);

    throw new NotSupportedException(
      $"RoQ signature chunk 0x{RoqChunkType.SIGNATURE:X4} has size 0x{size:X8}; only 0xFFFFFFFF and the older zero-sized form are defined.");
  }

  private static Summary _Summarise(ReadOnlyMemory<byte> data, int frameRate, int motionScale, bool extended) {
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
        case RoqChunkType.JPEG:
          ++frames;
          break;
        case RoqChunkType.HANG when extended:
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
      throw new InvalidDataException("No RoQ_INFO chunk (0x1001) was found, so the video dimensions are unknown.");
    return new(width, height, frames, hasAudio, audioIsStereo, frameRate, motionScale, extended);
  }

  private static (int Width, int Height) _ReadInfo(ReadOnlySpan<byte> data, ChunkHeader chunk) {
    if (chunk.Size < _InfoLength)
      throw new InvalidDataException($"A RoQ_INFO chunk is {chunk.Size} bytes, short of its eight-byte payload.");
    var payload = data.Slice(chunk.PayloadOffset, _InfoLength);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
    var macroblock = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
    var subblock = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException($"RoQ_INFO states a picture of {width}x{height}, which has no pixels.");
    if (macroblock != 8 || subblock != 4)
      throw new NotSupportedException($"RoQ_INFO states block fields {macroblock}/{subblock}; only the defined 8/4 quadtree is supported.");
    return (width, height);
  }

  private static IEnumerable<ChunkHeader> _WalkHeaders(ReadOnlyMemory<byte> data) {
    var at = _HeaderLength;
    while (at < data.Length) {
      if (at + _HeaderLength > data.Length)
        throw new InvalidDataException($"A RoQ chunk header starts at byte {at} with only {data.Length - at} bytes left.");
      var span = data.Span[at..];
      var id = BinaryPrimitives.ReadUInt16LittleEndian(span);
      var size = BinaryPrimitives.ReadUInt32LittleEndian(span[2..]);
      var argument = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]);
      var payloadOffset = at + _HeaderLength;

      if (id == RoqChunkType.PACKET) {
        yield return new(id, size, argument, payloadOffset);
        at = payloadOffset;
        continue;
      }

      if (size > int.MaxValue || payloadOffset + (long)size > data.Length)
        throw new InvalidDataException($"RoQ chunk 0x{id:X4} at byte {at} states {size} payload bytes past the file end.");
      yield return new(id, size, argument, payloadOffset);
      at = payloadOffset + (int)size;
    }
  }

  internal static IEnumerable<CodedPacket> ReadPackets(RoqContainer container) {
    var data = container.Data;
    var audioStreamIndex = container.HasAudio ? 1 : -1;
    var videoFrame = 0L;
    var havePicture = false;
    long audioSample = 0;

    foreach (var chunk in _WalkHeaders(data)) {
      var payload = chunk.Id == RoqChunkType.PACKET ? ReadOnlyMemory<byte>.Empty : data.Slice(chunk.PayloadOffset, (int)chunk.Size);
      switch (chunk.Id) {
        case RoqChunkType.INFO:
        case RoqChunkType.QUAD_CODEBOOK:
          yield return new(StreamIndex: 0, Data: _WithHeader(data, chunk));
          break;

        case RoqChunkType.QUAD_VQ:
        case RoqChunkType.JPEG:
          yield return _PicturePacket(data, chunk, videoFrame++, chunk.Id == RoqChunkType.JPEG || !havePicture);
          havePicture = true;
          break;

        case RoqChunkType.HANG when container.IsExtendedProfile:
          yield return _PicturePacket(data, chunk, videoFrame++, !havePicture);
          havePicture = true;
          break;

        case RoqChunkType.SOUND_MONO:
          if (audioStreamIndex >= 0) {
            yield return new(StreamIndex: audioStreamIndex, Data: payload, PresentationTimestamp: audioSample, IsKeyFrame: true, ContainerPrivateData: data.Slice(chunk.PayloadOffset - 2, 2));
            audioSample += payload.Length;
          }
          break;

        case RoqChunkType.SOUND_STEREO:
          if (audioStreamIndex >= 0) {
            yield return new(StreamIndex: audioStreamIndex, Data: payload, PresentationTimestamp: audioSample, IsKeyFrame: true, ContainerPrivateData: data.Slice(chunk.PayloadOffset - 2, 2));
            audioSample += payload.Length / 2;
          }
          break;
      }
    }
  }

  private static CodedPacket _PicturePacket(ReadOnlyMemory<byte> data, ChunkHeader chunk, long frame, bool key)
    => new(StreamIndex: 0, Data: _WithHeader(data, chunk), PresentationTimestamp: frame, DecodeTimestamp: frame, Duration: 1, IsKeyFrame: key);

  private static ReadOnlyMemory<byte> _WithHeader(ReadOnlyMemory<byte> data, ChunkHeader chunk)
    => data.Slice(chunk.PayloadOffset - _HeaderLength, _HeaderLength + (int)chunk.Size);
}
