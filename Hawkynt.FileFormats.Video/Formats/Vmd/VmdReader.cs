using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vmd;

/// <summary>Reads the classic 816-byte Sierra VMD container and its fixed block/part table.</summary>
/// <remarks>
/// The fixed table walk is converted from FFmpeg's LGPL-2.1-or-later <c>libavformat/sierravmd.c</c>;
/// see <c>Codecs/Vmd/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. Every block supplies its own absolute data
/// offset and owns exactly the number of part records stated at header offset 18. Unknown part types
/// are skipped but still consume their declared data length, matching the format's role as a container
/// for subtitles and embedded-file records as well as audio/video.
/// </remarks>
internal static class VmdReader {

  private const int _HEADER_LENGTH = 816;
  private const ushort _EXPECTED_HEADER_LENGTH_FIELD = 814;
  private const int _BLOCK_RECORD_LENGTH = 6;
  private const int _FRAME_RECORD_LENGTH = 16;
  private const int _MAX_DIMENSION = 4096;

  private const int _OFFSET_HEADER_LENGTH_FIELD = 0;
  private const int _OFFSET_CODEC_VERSION = 4;
  private const int _OFFSET_NUM_BLOCKS = 6;
  private const int _OFFSET_WIDTH = 12;
  private const int _OFFSET_HEIGHT = 14;
  private const int _OFFSET_FLAGS = 16;
  private const int _OFFSET_FRAMES_PER_BLOCK = 18;
  private const int _OFFSET_MULTIMEDIA_DATA_OFFSET = 20;
  private const int _OFFSET_VIDEO_CODEC = 24;
  private const int _OFFSET_AUDIO_SAMPLE_RATE = 804;
  private const int _OFFSET_AUDIO_FRAME_LENGTH = 806;
  private const int _OFFSET_TOC_OFFSET = 812;

  private const ushort _FLAG_HAS_SOUND = 0x1000;
  private const byte _FRAME_TYPE_AUDIO = 1;
  private const byte _FRAME_TYPE_VIDEO = 2;

  internal static bool LooksPlausible(ReadOnlySpan<byte> header) {
    if (header.Length < _HEADER_LENGTH)
      return false;
    if (BinaryPrimitives.ReadUInt16LittleEndian(header[_OFFSET_HEADER_LENGTH_FIELD..]) != _EXPECTED_HEADER_LENGTH_FIELD)
      return false;

    var width = BinaryPrimitives.ReadUInt16LittleEndian(header[_OFFSET_WIDTH..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(header[_OFFSET_HEIGHT..]);
    if (width > _MAX_DIMENSION || height > _MAX_DIMENSION)
      return false;

    return BinaryPrimitives.ReadUInt32LittleEndian(header[_OFFSET_MULTIMEDIA_DATA_OFFSET..]) == _HEADER_LENGTH;
  }

  internal static VmdContainer Open(ReadOnlyMemory<byte> data) {
    if (!LooksPlausible(data.Span))
      throw new NotSupportedException(
        "This file is not the classic 816-byte Sierra VMD form: its 814-byte header-length field, "
        + "picture geometry or multimedia-data offset does not match that layout.");

    var span = data.Span;
    var width = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_WIDTH..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_HEIGHT..]);
    var numBlocks = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_NUM_BLOCKS..]);
    var framesPerBlock = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_FRAMES_PER_BLOCK..]);
    var flags = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_FLAGS..]);
    var multimediaOffset = BinaryPrimitives.ReadUInt32LittleEndian(span[_OFFSET_MULTIMEDIA_DATA_OFFSET..]);
    var audioSampleRate = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_AUDIO_SAMPLE_RATE..]);
    var audioFrameLengthRaw = BinaryPrimitives.ReadInt16LittleEndian(span[_OFFSET_AUDIO_FRAME_LENGTH..]);
    var tocOffset = BinaryPrimitives.ReadUInt32LittleEndian(span[_OFFSET_TOC_OFFSET..]);
    var codecVersion = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_CODEC_VERSION..]);

    if (tocOffset < multimediaOffset || tocOffset > data.Length)
      throw new InvalidDataException(
        $"The VMD table of contents offset {tocOffset} is outside the multimedia-data range of this {data.Length}-byte file.");

    var blockTableLength = checked((long)numBlocks * _BLOCK_RECORD_LENGTH);
    var frameTableStart = checked((long)tocOffset + blockTableLength);
    if (frameTableStart > data.Length)
      throw new InvalidDataException(
        $"The VMD block table ({numBlocks} six-byte records) runs past the end of the file.");

    int frameCount;
    var legacyFlatTable = framesPerBlock == 0;
    if (legacyFlatTable) {
      var bytes = data.Length - frameTableStart;
      if (bytes % _FRAME_RECORD_LENGTH != 0)
        throw new InvalidDataException(
          $"The legacy VMD frame table occupies {bytes} bytes, not a whole number of sixteen-byte records.");
      frameCount = checked((int)(bytes / _FRAME_RECORD_LENGTH));
    } else {
      var count = checked((long)numBlocks * framesPerBlock);
      if (count > int.MaxValue)
        throw new InvalidDataException("The VMD block/part table contains more records than can be indexed in memory.");
      frameCount = (int)count;
      var frameTableEnd = checked(frameTableStart + count * _FRAME_RECORD_LENGTH);
      if (frameTableEnd > data.Length)
        throw new InvalidDataException(
          $"The VMD frame table needs {count} sixteen-byte records and runs past the end of the file.");
    }

    var blockOffsets = new int[numBlocks];
    for (var block = 0; block < numBlocks; ++block) {
      var at = checked((int)tocOffset + block * _BLOCK_RECORD_LENGTH + 2);
      var value = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(at, 4));
      if (value < multimediaOffset || value > tocOffset)
        throw new InvalidDataException(
          $"VMD block {block} starts at {value}, outside the multimedia-data area {multimediaOffset}..{tocOffset}.");
      if (block > 0 && value < blockOffsets[block - 1])
        throw new InvalidDataException(
          $"VMD block {block} starts at {value}, before block {block - 1} at {blockOffsets[block - 1]}.");
      blockOffsets[block] = checked((int)value);
    }

    var frameOffsets = new int[frameCount];
    var frameBlocks = new int[frameCount];
    var videoFrameCount = 0;
    var hasAudioRecord = false;

    if (legacyFlatTable)
      _ReadLegacyFlatTable(
        span, checked((int)frameTableStart), frameCount, multimediaOffset, tocOffset, blockOffsets,
        frameOffsets, frameBlocks, ref videoFrameCount, ref hasAudioRecord);
    else
      _ReadFixedTable(
        span, checked((int)frameTableStart), numBlocks, framesPerBlock, tocOffset, blockOffsets,
        frameOffsets, frameBlocks, ref videoFrameCount, ref hasAudioRecord);

    var isIndeo3 = _IsIndeo3(span);
    var finalWidth = width;
    var finalHeight = height;
    if (isIndeo3 && finalWidth > 320) {
      finalWidth /= 2;
      finalHeight /= 2;
    }

    return new() {
      Data = data,
      Width = finalWidth,
      Height = finalHeight,
      VideoFrameCount = videoFrameCount,
      HasAudio = hasAudioRecord && (flags & _FLAG_HAS_SOUND) != 0 && audioSampleRate != 0,
      AudioSampleRate = audioSampleRate,
      AudioFrameLength = Math.Abs(audioFrameLengthRaw),
      CodecVersion = codecVersion,
      IsIndeo3 = isIndeo3,
      TocOffset = tocOffset,
      NumBlocks = numBlocks,
      FramesPerBlock = framesPerBlock,
      FrameCount = frameCount,
      FrameTableStart = checked((int)frameTableStart),
      FrameDataOffsets = frameOffsets,
      FrameBlockIndices = frameBlocks,
      MultimediaDataOffset = multimediaOffset,
      HeaderPayload = data[.._HEADER_LENGTH],
      BlockOffsets = blockOffsets,
    };
  }

  internal static IEnumerable<CodedPacket> ReadPackets(VmdContainer container) {
    var data = container.Data;
    var firstVideo = true;

    for (var i = 0; i < container.FrameCount; ++i) {
      var recordOffset = container.FrameTableStart + i * _FRAME_RECORD_LENGTH;
      var record = data.Slice(recordOffset, _FRAME_RECORD_LENGTH);
      var type = record.Span[0];
      var length = BinaryPrimitives.ReadUInt32LittleEndian(record.Span[2..]);
      if (length == 0 && type != _FRAME_TYPE_AUDIO)
        continue;

      var dataOffset = container.FrameDataOffsets[i];
      if (type == _FRAME_TYPE_VIDEO) {
        var packetData = container.IsIndeo3
          ? data.Slice(dataOffset, checked((int)length))
          : _WithRecord(record, data, dataOffset, length);
        yield return new(
          StreamIndex: 0,
          Data: packetData,
          PresentationTimestamp: container.FrameBlockIndices[i],
          DecodeTimestamp: container.FrameBlockIndices[i],
          IsKeyFrame: firstVideo);
        firstVideo = false;
      } else if (type == _FRAME_TYPE_AUDIO && container.HasAudio) {
        yield return new(
          StreamIndex: 1,
          Data: _WithRecord(record, data, dataOffset, length),
          IsKeyFrame: true);
      }
    }
  }

  private static void _ReadFixedTable(
    ReadOnlySpan<byte> file, int frameTableStart, int numBlocks, int framesPerBlock, uint tocOffset,
    IReadOnlyList<int> blockOffsets, int[] frameOffsets, int[] frameBlocks,
    ref int videoFrameCount, ref bool hasAudioRecord) {
    for (var block = 0; block < numBlocks; ++block) {
      long currentOffset = blockOffsets[block];
      for (var part = 0; part < framesPerBlock; ++part) {
        var index = block * framesPerBlock + part;
        var record = file.Slice(frameTableStart + index * _FRAME_RECORD_LENGTH, _FRAME_RECORD_LENGTH);
        var type = record[0];
        var length = BinaryPrimitives.ReadUInt32LittleEndian(record[2..]);

        frameOffsets[index] = checked((int)currentOffset);
        frameBlocks[index] = block;
        if (currentOffset + length > tocOffset)
          throw new InvalidDataException(
            $"VMD block {block}, part {part} ends at {currentOffset + length}, beyond the table of contents at {tocOffset}.");

        if (type == _FRAME_TYPE_VIDEO && length != 0)
          ++videoFrameCount;
        else if (type == _FRAME_TYPE_AUDIO)
          hasAudioRecord = true;

        currentOffset += length;
      }

      if (block + 1 < numBlocks && currentOffset > blockOffsets[block + 1])
        throw new InvalidDataException(
          $"VMD block {block}'s parts run through {currentOffset}, overlapping block {block + 1} at {blockOffsets[block + 1]}.");
    }
  }

  private static void _ReadLegacyFlatTable(
    ReadOnlySpan<byte> file, int frameTableStart, int frameCount, uint multimediaOffset, uint tocOffset,
    IReadOnlyList<int> blockOffsets, int[] frameOffsets, int[] frameBlocks,
    ref int videoFrameCount, ref bool hasAudioRecord) {
    long currentOffset = multimediaOffset;
    var block = 0;
    for (var i = 0; i < frameCount; ++i) {
      while (block + 1 < blockOffsets.Count && blockOffsets[block + 1] <= currentOffset)
        ++block;

      var record = file.Slice(frameTableStart + i * _FRAME_RECORD_LENGTH, _FRAME_RECORD_LENGTH);
      var type = record[0];
      var length = BinaryPrimitives.ReadUInt32LittleEndian(record[2..]);
      frameOffsets[i] = checked((int)currentOffset);
      frameBlocks[i] = block;

      if (currentOffset + length > tocOffset)
        throw new InvalidDataException(
          $"Legacy VMD frame record {i} ends at {currentOffset + length}, beyond the table of contents at {tocOffset}.");
      if (type == _FRAME_TYPE_VIDEO && length != 0)
        ++videoFrameCount;
      else if (type == _FRAME_TYPE_AUDIO)
        hasAudioRecord = true;

      currentOffset += length;
    }

    if (currentOffset != tocOffset)
      throw new InvalidDataException(
        $"The legacy VMD record lengths end at {currentOffset}, not at the table of contents offset {tocOffset}.");
  }

  private static bool _IsIndeo3(ReadOnlySpan<byte> header)
    => _AsciiEqualsIgnoreCase(header[_OFFSET_VIDEO_CODEC], (byte)'i')
       && _AsciiEqualsIgnoreCase(header[_OFFSET_VIDEO_CODEC + 1], (byte)'v')
       && header[_OFFSET_VIDEO_CODEC + 2] == (byte)'3';

  private static bool _AsciiEqualsIgnoreCase(byte value, byte lower)
    => value == lower || value == lower - 32;

  private static ReadOnlyMemory<byte> _WithRecord(ReadOnlyMemory<byte> record, ReadOnlyMemory<byte> data, int offset, uint length) {
    var combined = new byte[checked(_FRAME_RECORD_LENGTH + (int)length)];
    record.Span.CopyTo(combined);
    data.Slice(offset, checked((int)length)).Span.CopyTo(combined.AsSpan(_FRAME_RECORD_LENGTH));
    return combined;
  }
}
