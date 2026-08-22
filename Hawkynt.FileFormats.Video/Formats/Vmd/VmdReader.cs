using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vmd;

/// <summary>
/// Splits a Sierra VMD file into its header, its table of contents and the packets those describe,
/// without reading a single LZ back-reference or a single palette byte's meaning.
/// </summary>
/// <remarks>
/// A file is an 816-byte header (a two-byte length field stating 814, then the fields themselves),
/// a run of coded data the header's own <c>multimedia data offset</c> field names the start of, and a
/// table of contents near the end of the file: a block offset table, then a frame information table.
/// <para/>
/// "Block" and "frame" are two different things here and the format keeps them apart: a block is a
/// unit of interleaving — one video frame and the one or more audio frames that play alongside it —
/// and the block offset table exists only so a player can seek to one without walking every frame
/// before it. Nothing here needs that table for sequential reading: every frame information record
/// states its own data length, so a running cursor that starts at the header's multimedia data offset
/// and advances by each record's stated length in turn lands on every frame's own bytes in order — and,
/// measured against every file this reader was built against, lands exactly on the table of contents'
/// own offset once every record has been walked. That agreement is checked rather than assumed: a file
/// whose lengths do not sum to its own table of contents offset is refused, because a cursor that
/// landed anywhere else would be handing out some other frame's bytes under the wrong header.
/// <para/>
/// A frame information record names one of two kinds — audio or video — and this reader has met a
/// third: a record of type zero and length zero, a handful of times, always contributing nothing to
/// the cursor. It is skipped rather than refused, since it is indistinguishable from a placeholder no
/// packet needs to come from. A record of any other type, or a zero-typed record with a nonzero length,
/// is refused: nothing measured here explains what such a record would mean, and guessing would hand a
/// decoder bytes under the wrong description.
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
  private const int _OFFSET_MULTIMEDIA_DATA_OFFSET = 20;
  private const int _OFFSET_AUDIO_SAMPLE_RATE = 804;
  private const int _OFFSET_AUDIO_FRAME_LENGTH = 806;
  private const int _OFFSET_TOC_OFFSET = 812;

  private const ushort _FLAG_HAS_SOUND = 0x1000;

  private const byte _FRAME_TYPE_AUDIO = 1;
  private const byte _FRAME_TYPE_VIDEO = 2;

  /// <summary>
  /// Whether a header looks like a Sierra VMD file's: the fixed 814-byte length field every real
  /// sample states, a picture size within bounds no real file exceeds (or none at all — VMD carries
  /// sound-only recordings), and a multimedia data offset that lands exactly where this fixed-size
  /// header ends. VMD carries no signature of its own, so this is the only check a container can make.
  /// </summary>
  internal static bool LooksPlausible(ReadOnlySpan<byte> header) {
    if (header.Length < _HEADER_LENGTH)
      return false;

    if (BinaryPrimitives.ReadUInt16LittleEndian(header[_OFFSET_HEADER_LENGTH_FIELD..]) != _EXPECTED_HEADER_LENGTH_FIELD)
      return false;

    var width = BinaryPrimitives.ReadUInt16LittleEndian(header[_OFFSET_WIDTH..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(header[_OFFSET_HEIGHT..]);
    if (width > _MAX_DIMENSION || height > _MAX_DIMENSION)
      return false;

    var multimediaOffset = BinaryPrimitives.ReadUInt32LittleEndian(header[_OFFSET_MULTIMEDIA_DATA_OFFSET..]);
    return multimediaOffset == _HEADER_LENGTH;
  }

  internal static VmdContainer Open(ReadOnlyMemory<byte> data) {
    if (!LooksPlausible(data.Span))
      throw new NotSupportedException(
        "This file's header does not state the fixed 814-byte length field every Sierra VMD file "
        + "opens with, or its multimedia data offset does not land at byte 816 where that fixed-size "
        + "header ends. This is not a Sierra VMD file, or is a header variant — a 52-byte header "
        + "omitting the palette, or one carrying an external audio codec's extra fields — this reader "
        + "does not read; only the classic 816-byte form is implemented.");

    var span = data.Span;
    var width = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_WIDTH..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_HEIGHT..]);
    var numBlocks = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_NUM_BLOCKS..]);
    var flags = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_FLAGS..]);
    var multimediaOffset = BinaryPrimitives.ReadUInt32LittleEndian(span[_OFFSET_MULTIMEDIA_DATA_OFFSET..]);
    var audioSampleRate = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_AUDIO_SAMPLE_RATE..]);
    var audioFrameLengthRaw = BinaryPrimitives.ReadInt16LittleEndian(span[_OFFSET_AUDIO_FRAME_LENGTH..]);
    var tocOffset = BinaryPrimitives.ReadUInt32LittleEndian(span[_OFFSET_TOC_OFFSET..]);
    var codecVersion = BinaryPrimitives.ReadUInt16LittleEndian(span[_OFFSET_CODEC_VERSION..]);

    if (tocOffset > data.Length)
      throw new InvalidDataException(
        $"The table of contents offset ({tocOffset}) is past the end of a file of {data.Length} bytes.");

    var blockTableLength = (long)numBlocks * _BLOCK_RECORD_LENGTH;
    var frameTableStart = tocOffset + blockTableLength;
    if (frameTableStart > data.Length)
      throw new InvalidDataException(
        $"The block offset table ({numBlocks} records of six bytes, starting at {tocOffset}) runs past "
        + $"the end of a file of {data.Length} bytes.");

    var frameTableBytes = data.Length - frameTableStart;
    if (frameTableBytes % _FRAME_RECORD_LENGTH != 0)
      throw new InvalidDataException(
        $"The frame information table, {frameTableBytes} bytes starting at {frameTableStart}, is not a "
        + "whole number of sixteen-byte records.");

    var frameCount = (int)(frameTableBytes / _FRAME_RECORD_LENGTH);

    var blockOffsets = new int[numBlocks];
    for (var b = 0; b < numBlocks; ++b) {
      var blockRecordOffset = (int)tocOffset + b * _BLOCK_RECORD_LENGTH;
      var blockOffset = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(blockRecordOffset + 2, 4));
      if (blockOffset > data.Length)
        throw new InvalidDataException(
          $"Block offset table record {b} states an absolute offset of {blockOffset}, past the end of "
          + $"a file of {data.Length} bytes.");
      if (b > 0 && blockOffset < blockOffsets[b - 1])
        throw new InvalidDataException(
          $"Block offset table record {b} states offset {blockOffset}, before record {b - 1}'s "
          + $"{blockOffsets[b - 1]}. The table this reader uses to number a video frame by the block "
          + "it belongs to is expected to be non-decreasing.");
      blockOffsets[b] = (int)blockOffset;
    }

    var cursor = (long)multimediaOffset;
    var videoFrameCount = 0;
    var hasAudio = false;
    var declaredWidth = width;
    var declaredHeight = height;

    for (var i = 0; i < frameCount; ++i) {
      var recordOffset = (int)(frameTableStart + (long)i * _FRAME_RECORD_LENGTH);
      var record = span.Slice(recordOffset, _FRAME_RECORD_LENGTH);
      var type = record[0];
      var length = BinaryPrimitives.ReadUInt32LittleEndian(record[2..]);

      if (type != _FRAME_TYPE_AUDIO && type != _FRAME_TYPE_VIDEO) {
        if (type != 0 || length != 0)
          throw new InvalidDataException(
            $"Frame information record {i} states type {type}, which is neither 1 (audio) nor 2 "
            + $"(video), and is not the zero-length placeholder this reader passes over.");
      } else {
        if (cursor + length > data.Length)
          throw new InvalidDataException(
            $"Frame information record {i} (type {type}) states {length} bytes of data starting at "
            + $"{cursor}, which runs past the end of a file of {data.Length} bytes.");

        if (type == _FRAME_TYPE_VIDEO) {
          ++videoFrameCount;
          if (declaredWidth == 0 && length >= 8) {
            var payloadStart = (int)cursor;
            var left = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(payloadStart, 2));
            var top = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(payloadStart + 2, 2));
            var right = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(payloadStart + 4, 2));
            var bottom = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(payloadStart + 6, 2));
            if (right >= left && bottom >= top) {
              declaredWidth = (ushort)(right - left + 1);
              declaredHeight = (ushort)(bottom - top + 1);
            }
          }
        } else {
          hasAudio = true;
        }
      }

      cursor += length;
    }

    if (cursor != tocOffset)
      throw new InvalidDataException(
        $"Every frame information record's stated length sums to a cursor of {cursor}, which does not "
        + $"land on the table of contents' own offset ({tocOffset}). The frame data and the table that "
        + "describes it disagree about where one ends and the other begins.");

    var finalWidth = width != 0 ? width : declaredWidth;
    var finalHeight = height != 0 ? height : declaredHeight;

    return new() {
      Data = data,
      Width = finalWidth,
      Height = finalHeight,
      VideoFrameCount = videoFrameCount,
      HasAudio = hasAudio && (flags & _FLAG_HAS_SOUND) != 0 && audioSampleRate != 0,
      AudioSampleRate = audioSampleRate,
      AudioFrameLength = Math.Abs(audioFrameLengthRaw),
      CodecVersion = codecVersion,
      TocOffset = tocOffset,
      NumBlocks = numBlocks,
      FrameCount = frameCount,
      FrameTableStart = (int)frameTableStart,
      MultimediaDataOffset = multimediaOffset,
      HeaderPayload = data[.._HEADER_LENGTH],
      BlockOffsets = blockOffsets,
    };
  }

  /// <summary>Walks the frame information table a second time, handing out the packets a caller can do
  /// anything with — sequentially, from the same cursor arithmetic <see cref="Open"/> already checked
  /// sums correctly, so this cannot land on the wrong bytes without <see cref="Open"/> having refused
  /// the file first.</summary>
  internal static IEnumerable<CodedPacket> ReadPackets(VmdContainer container) {
    var data = container.Data;
    var blockOffsets = container.BlockOffsets;

    long cursor = container.MultimediaDataOffset;
    var isFirstVideoFrame = true;
    var blockIndex = 0;

    for (var i = 0; i < container.FrameCount; ++i) {
      var recordOffset = container.FrameTableStart + i * _FRAME_RECORD_LENGTH;
      var recordMemory = data.Slice(recordOffset, _FRAME_RECORD_LENGTH);
      var type = recordMemory.Span[0];
      var length = BinaryPrimitives.ReadUInt32LittleEndian(recordMemory.Span[2..]);

      while (blockIndex + 1 < blockOffsets.Count && blockOffsets[blockIndex + 1] <= cursor)
        ++blockIndex;

      if (type == _FRAME_TYPE_VIDEO) {
        yield return new(
          StreamIndex: 0,
          Data: _WithRecord(recordMemory, data, cursor, length),
          PresentationTimestamp: blockIndex,
          DecodeTimestamp: blockIndex,
          IsKeyFrame: isFirstVideoFrame);
        isFirstVideoFrame = false;
      } else if (type == _FRAME_TYPE_AUDIO && container.HasAudio) {
        yield return new(
          StreamIndex: 1,
          Data: _WithRecord(recordMemory, data, cursor, length),
          IsKeyFrame: true);
      }

      cursor += length;
    }
  }

  /// <summary>Prepends the sixteen-byte frame information record to the frame's own data. The record
  /// and the data it describes are not adjacent in the file — the record lives in the table of
  /// contents near the end, the data wherever its block was written — so, unlike a container whose
  /// packet already carries its header in front of it, this is a real copy rather than a wider window
  /// onto the same bytes. What it buys is the same thing RoQ's and MVE's own headers-kept-in-front buy:
  /// a decoder reads a video frame's rectangle and its new-palette flag from the packet handed to it,
  /// without the container having to understand what either of those mean.</summary>
  private static ReadOnlyMemory<byte> _WithRecord(ReadOnlyMemory<byte> record, ReadOnlyMemory<byte> data, long cursor, uint length) {
    var combined = new byte[_FRAME_RECORD_LENGTH + length];
    record.Span.CopyTo(combined);
    data.Slice((int)cursor, (int)length).Span.CopyTo(combined.AsSpan(_FRAME_RECORD_LENGTH));
    return combined;
  }
}
