using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.InterplayMve;

/// <summary>
/// Splits an Interplay MVE file into the chunks and opcodes it is built from, without decoding a
/// single 8x8 block or audio delta.
/// </summary>
/// <remarks>
/// A file is a twenty-six-byte header and then a flat run of chunks, each an outer four-byte header
/// wrapping an opcode stream. Chunk kinds and opcode kinds are two different numbering spaces reusing
/// small integers, which is why <see cref="MveChunkType"/> and <see cref="MveOpcodeType"/> stay apart.
/// <para/>
/// Interplay has three video-data opcodes. 0x06 carries its own 16-bit map, 0x10 consumes the most
/// recent skip and decoding maps, and 0x11 is the normal four-bit-per-block Interplay Video stream.
/// <c>SEND_BUFFER</c> is retained as codec state too: it is what the original player and FFmpeg use
/// to distinguish a reconstructed back buffer from a picture actually sent to the display.
/// </remarks>
internal static class MveReader {

  private static readonly byte[] _Signature = "Interplay MVE File\x1A\0"u8.ToArray();
  private const int _HEADER_LENGTH = 26;
  private const int _CHUNK_HEADER_LENGTH = 4;
  private const int _OPCODE_HEADER_LENGTH = 4;

  internal readonly record struct OpcodeHeader(ushort Length, byte Type, byte Version, int PayloadOffset);

  internal readonly record struct Summary(
    int Width, int Height, int VideoBitsPerPixel, int VideoFrameCount, bool HasAudio,
    bool AudioIsStereo, bool AudioIs16Bit, int AudioSampleRate, long FrameDurationMicroseconds);

  internal static MveContainer Open(ReadOnlyMemory<byte> data) {
    if (data.Length < _HEADER_LENGTH || !data.Span[.._Signature.Length].SequenceEqual(_Signature))
      throw new NotSupportedException(
        "The file does not open with the twenty-byte \"Interplay MVE File\" signature. This is not an "
        + "Interplay MVE file.");

    var summary = _Summarise(data);
    return new() {
      Data = data,
      Width = summary.Width,
      Height = summary.Height,
      VideoBitsPerPixel = summary.VideoBitsPerPixel,
      VideoFrameCount = summary.VideoFrameCount,
      HasAudio = summary.HasAudio,
      AudioIsStereo = summary.AudioIsStereo,
      AudioIs16Bit = summary.AudioIs16Bit,
      AudioSampleRate = summary.AudioSampleRate,
      FrameDurationMicroseconds = summary.FrameDurationMicroseconds,
    };
  }

  private static Summary _Summarise(ReadOnlyMemory<byte> data) {
    var width = 0;
    var height = 0;
    var videoBitsPerPixel = 8;
    var haveSize = false;
    var frames = 0;
    var hasAudio = false;
    var audioIsStereo = false;
    var audioIs16Bit = false;
    var audioSampleRate = 0;
    long frameDuration = 0;

    foreach (var (_, chunkPayload, chunkOffset) in _WalkChunks(data))
      foreach (var opcode in _WalkOpcodes(data, chunkOffset, chunkPayload)) {
        switch (opcode.Type) {
          case MveOpcodeType.INIT_VIDEO_BUFFERS:
            if (!haveSize)
              (width, height, videoBitsPerPixel) = _ReadVideoBufferSize(data.Span, opcode);
            haveSize = true;
            break;
          case MveOpcodeType.VIDEO_DATA_06:
          case MveOpcodeType.VIDEO_DATA_10:
          case MveOpcodeType.VIDEO_DATA_11:
            ++frames;
            break;
          case MveOpcodeType.INIT_AUDIO_BUFFERS:
            hasAudio = true;
            (audioIsStereo, audioIs16Bit, audioSampleRate) = _ReadAudioInit(data.Span, opcode);
            break;
          case MveOpcodeType.CREATE_TIMER:
            frameDuration = _ReadTimer(data.Span, opcode);
            break;
        }
      }

    if (!haveSize)
      throw new InvalidDataException(
        "No INIT_VIDEO_BUFFERS opcode (0x05) was found anywhere in the file. Every picture states its "
        + "dimensions in that opcode's chunk and nowhere else, so a file without one cannot be sized.");

    return new(width, height, videoBitsPerPixel, frames, hasAudio, audioIsStereo, audioIs16Bit, audioSampleRate, frameDuration);
  }

  /// <summary>Reads the picture dimensions and whether version 2 selected the RGB555 true-colour path.</summary>
  private static (int Width, int Height, int BitsPerPixel) _ReadVideoBufferSize(ReadOnlySpan<byte> data, OpcodeHeader opcode) {
    if (opcode.Length < 4)
      throw new InvalidDataException($"An INIT_VIDEO_BUFFERS opcode is {opcode.Length} bytes, short of the four a picture size needs.");
    if (opcode.Version >= 2 && opcode.Length < 8)
      throw new InvalidDataException(
        $"A version-{opcode.Version} INIT_VIDEO_BUFFERS opcode is {opcode.Length} bytes, short of the eight fields version 2 needs.");

    var payload = data.Slice(opcode.PayloadOffset, opcode.Length);
    var widthBlocks = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var heightBlocks = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
    if (widthBlocks == 0 || heightBlocks == 0)
      throw new InvalidDataException($"INIT_VIDEO_BUFFERS states a picture of {widthBlocks}x{heightBlocks} macroblocks, which has no pixels.");

    var trueColour = opcode.Version >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) != 0;
    return (widthBlocks * 8, heightBlocks * 8, trueColour ? 16 : 8);
  }

  private static (bool Stereo, bool Is16Bit, int SampleRate) _ReadAudioInit(ReadOnlySpan<byte> data, OpcodeHeader opcode) {
    if (opcode.Length < 8)
      throw new InvalidDataException($"An INIT_AUDIO_BUFFERS opcode is {opcode.Length} bytes, short of the eight its fields need.");

    var payload = data.Slice(opcode.PayloadOffset, opcode.Length);
    var flags = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
    var sampleRate = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
    return ((flags & 1) != 0, (flags & 2) != 0, sampleRate);
  }

  private static long _ReadTimer(ReadOnlySpan<byte> data, OpcodeHeader opcode) {
    if (opcode.Length < 6)
      throw new InvalidDataException($"A CREATE_TIMER opcode is {opcode.Length} bytes, short of the six its fields need.");

    var payload = data.Slice(opcode.PayloadOffset, opcode.Length);
    var rate = BinaryPrimitives.ReadUInt32LittleEndian(payload);
    var subdivision = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
    return (long)rate * subdivision;
  }

  private static IEnumerable<(ushort Type, ReadOnlyMemory<byte> Payload, int Offset)> _WalkChunks(ReadOnlyMemory<byte> data) {
    var at = _HEADER_LENGTH;

    while (at < data.Length) {
      if (at + _CHUNK_HEADER_LENGTH > data.Length)
        throw new InvalidDataException(
          $"A chunk header would start at byte {at}, {data.Length - at} bytes from the end of a file "
          + "whose chunk headers are four bytes each.");

      var length = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[at..]);
      var type = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(at + 2)..]);
      var payloadOffset = at + _CHUNK_HEADER_LENGTH;

      if (payloadOffset + length > data.Length)
        throw new InvalidDataException(
          $"A chunk of type {type} at byte {at} states a payload of {length} bytes, which runs past "
          + $"the file's {data.Length} bytes.");

      yield return (type, data.Slice(payloadOffset, length), payloadOffset);
      at = payloadOffset + length;
    }
  }

  private static IEnumerable<OpcodeHeader> _WalkOpcodes(ReadOnlyMemory<byte> data, int chunkOffset, ReadOnlyMemory<byte> chunkPayload) {
    var at = chunkOffset;
    var end = chunkOffset + chunkPayload.Length;

    while (at < end) {
      if (at + _OPCODE_HEADER_LENGTH > end)
        throw new InvalidDataException(
          $"An opcode header would start at byte {at}, past the end of the chunk that holds it.");

      var length = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[at..]);
      var type = data.Span[at + 2];
      var version = data.Span[at + 3];
      var payloadOffset = at + _OPCODE_HEADER_LENGTH;

      if (payloadOffset + length > end)
        throw new InvalidDataException(
          $"An opcode of type 0x{type:X2} at byte {at} states a payload of {length} bytes, which runs "
          + "past the end of the chunk that holds it.");

      yield return new(length, type, version, payloadOffset);
      at = payloadOffset + length;
    }
  }

  /// <summary>Hands codec-owned opcodes out as packets without interpreting their block data.</summary>
  internal static IEnumerable<CodedPacket> ReadPackets(MveContainer container) {
    var data = container.Data;
    var audioStreamIndex = container.HasAudio ? 1 : -1;

    long videoTimestamp = 0;
    long audioTimestamp = 0;
    var isFirstPicture = true;

    foreach (var (_, chunkPayload, chunkOffset) in _WalkChunks(data))
      foreach (var opcode in _WalkOpcodes(data, chunkOffset, chunkPayload)) {
        switch (opcode.Type) {
          case MveOpcodeType.INIT_VIDEO_BUFFERS:
          case MveOpcodeType.SET_PALETTE:
          case MveOpcodeType.SET_PALETTE_COMPRESSED:
          case MveOpcodeType.SKIP_MAP:
          case MveOpcodeType.DECODING_MAP:
          case MveOpcodeType.SEND_BUFFER:
            yield return new(StreamIndex: 0, Data: _WithHeader(data, opcode));
            break;

          case MveOpcodeType.VIDEO_DATA_06:
          case MveOpcodeType.VIDEO_DATA_10:
          case MveOpcodeType.VIDEO_DATA_11:
            yield return new(
              StreamIndex: 0,
              Data: _WithHeader(data, opcode),
              PresentationTimestamp: videoTimestamp,
              DecodeTimestamp: videoTimestamp,
              Duration: container.FrameDurationMicroseconds,
              IsKeyFrame: isFirstPicture);
            videoTimestamp += container.FrameDurationMicroseconds;
            isFirstPicture = false;
            break;

          case MveOpcodeType.AUDIO_FRAME:
          case MveOpcodeType.AUDIO_SILENCE:
            if (audioStreamIndex >= 0) {
              var sampleCount = _ReadAudioSampleCount(data.Span, opcode);
              yield return new(StreamIndex: audioStreamIndex, Data: _WithHeader(data, opcode), PresentationTimestamp: audioTimestamp, IsKeyFrame: true);
              audioTimestamp += sampleCount;
            }
            break;
        }
      }
  }

  private static int _ReadAudioSampleCount(ReadOnlySpan<byte> span, OpcodeHeader opcode) {
    if (opcode.Length < 6)
      throw new InvalidDataException($"An audio frame opcode is {opcode.Length} bytes, short of the six its own header needs.");

    return BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(opcode.PayloadOffset + 4, 2));
  }

  private static ReadOnlyMemory<byte> _WithHeader(ReadOnlyMemory<byte> data, OpcodeHeader opcode)
    => data.Slice(opcode.PayloadOffset - _OPCODE_HEADER_LENGTH, _OPCODE_HEADER_LENGTH + opcode.Length);
}
