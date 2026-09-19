using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FlicVideo;

/// <summary>
/// Splits an Autodesk/DTA FLIC file into the frame chunks it is made of, without reading a single
/// image opcode inside any of them.
/// </summary>
internal static class FliReader {

  /// <summary>The header magic of the original Autodesk Animator format.</summary>
  internal const ushort MAGIC_FLI = 0xAF11;

  /// <summary>Autodesk Animator Pro FLC and the two 15-bit FLX variants.</summary>
  internal const ushort MAGIC_FLC = 0xAF12;

  /// <summary>Dave's Targa Animator extended FLIC, used for non-eight-bit colour depths.</summary>
  internal const ushort MAGIC_DTA = 0xAF44;

  internal const int HEADER_SIZE = 128;
  private const int _FRAME_HEADER_SIZE = 16;
  private const ushort _FRAME_MAGIC = 0xF1FA;

  internal readonly record struct Header(
    ushort Magic,
    ushort FrameCount,
    int Width,
    int Height,
    ushort Depth,
    uint Speed,
    int FirstFrameOffset);

  internal static FliContainer Open(ReadOnlyMemory<byte> data) {
    var header = ReadHeader(data.Span);
    return new() {
      Data = data,
      Magic = header.Magic,
      Width = header.Width,
      Height = header.Height,
      Depth = header.Depth,
      FrameCount = header.FrameCount,
      Speed = header.Speed,
      FirstFrameOffset = header.FirstFrameOffset,
    };
  }

  internal static Header ReadHeader(ReadOnlySpan<byte> data) {
    if (data.Length < HEADER_SIZE)
      throw new InvalidDataException(
        $"A FLIC file is {data.Length} bytes, short of the 128-byte header every one of them opens with.");

    var magic = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
    if (magic is not (MAGIC_FLI or MAGIC_FLC or MAGIC_DTA))
      throw new NotSupportedException(
        $"The file states magic 0x{magic:X4} at offset 4. Only 0x{MAGIC_FLI:X4} (.fli), 0x{MAGIC_FLC:X4} "
        + $"(.flc/.flx) and 0x{MAGIC_DTA:X4} (DTA extended .flh/.flt) are read; Huffman/BWT and frame-shift "
        + "FLIC variants use different bitstreams and are refused rather than guessed at.");

    var frameCount = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
    var storedDepth = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
    var speed = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);

    if (width <= 0 || height <= 0)
      throw new InvalidOperationException(
        $"The file states a picture of {width}x{height}, which has no pixels.");

    // A few old writers leave depth zero for the normal palettised form. Autodesk FLX is the other
    // historical oddity: it says 16 in the FLC-shaped header while its pixels are RGB555. DTA's AF44
    // is the format that genuinely distinguishes 15 from RGB565 16-bit pixels.
    var depth = storedDepth == 0 ? (ushort)8 : storedDepth;
    if (magic == MAGIC_FLC && depth == 16)
      depth = 15;

    var supportedDepth = magic switch {
      MAGIC_FLI => depth == 8,
      MAGIC_FLC => depth is 8 or 15,
      MAGIC_DTA => depth is 15 or 16 or 24,
      _ => false,
    };
    if (!supportedDepth)
      throw new NotSupportedException(
        $"The file states {storedDepth} bits per pixel under magic 0x{magic:X4}. This reader supports "
        + "8-bit FLI/FLC, Autodesk 15-bit FLX, and DTA 15/16/24-bit extended FLIC; that combination is not one of them.");

    // FLI has no frame-offset fields. FLC, FLX and AF44 DTA files share the FLC-shaped 128-byte
    // header and may place prefix data ahead of frame one.
    var oframe1 = magic == MAGIC_FLI ? 0u : BinaryPrimitives.ReadUInt32LittleEndian(data[80..]);
    var firstFrameOffset = oframe1 != 0 ? checked((int)oframe1) : HEADER_SIZE;

    if (firstFrameOffset < HEADER_SIZE || firstFrameOffset > data.Length)
      throw new InvalidDataException(
        $"The file's oframe1 field states the first frame begins at byte {firstFrameOffset}, which is "
        + $"{(firstFrameOffset < HEADER_SIZE ? "inside the 128-byte header" : $"past the file's {data.Length} bytes")}.");

    return new(magic, frameCount, width, height, depth, speed, firstFrameOffset);
  }

  /// <summary>
  /// Walks exactly <see cref="Header.FrameCount"/> <c>FRAME_TYPE</c> chunks from
  /// <see cref="Header.FirstFrameOffset"/>, handing out each one's sub-chunks as a packet.
  /// </summary>
  internal static IEnumerable<CodedPacket> Split(FliContainer container) {
    var data = container.Data;
    var offset = container.FirstFrameOffset;
    long presentation = 0;

    for (var frame = 0; frame < container.FrameCount; ++frame) {
      if (offset + _FRAME_HEADER_SIZE > data.Length)
        throw new InvalidDataException(
          $"Frame {frame} of {container.FrameCount} would start at byte {offset}, past the file's "
          + $"{data.Length} bytes. The header promises more frames than the file holds.");

      var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[offset..]);
      var magic = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(offset + 4)..]);
      if (magic != _FRAME_MAGIC)
        throw new InvalidDataException(
          $"Frame {frame} at byte {offset} states magic 0x{magic:X4} where a FRAME_TYPE chunk states "
          + $"0x{_FRAME_MAGIC:X4}. The frame chunks no longer line up, so nothing after this one can be trusted.");

      if (size < _FRAME_HEADER_SIZE || size > data.Length - offset)
        throw new InvalidDataException(
          $"Frame {frame} at byte {offset} states a size of {size} bytes, which "
          + (size < _FRAME_HEADER_SIZE ? "is shorter than a frame chunk's own 16-byte header." : "runs past the end of the file."));

      var delay = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(offset + 8)..]);
      var widthOverride = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(offset + 12)..]);
      var heightOverride = BinaryPrimitives.ReadUInt16LittleEndian(data.Span[(offset + 14)..]);
      if (widthOverride != 0 || heightOverride != 0)
        throw new NotSupportedException(
          $"Frame {frame} at byte {offset} states a picture size override of {widthOverride}x{heightOverride}. "
          + "That EGI overlay feature changes geometry midstream and is not a frame of the base video canvas.");

      var duration = delay != 0 ? delay : container.Speed;
      var payload = data.Slice(offset + _FRAME_HEADER_SIZE, checked((int)size) - _FRAME_HEADER_SIZE);
      var isKeyFrame = _CarriesWholeFramePicture(payload.Span);

      yield return new(
        StreamIndex: 0,
        Data: payload,
        PresentationTimestamp: presentation,
        DecodeTimestamp: presentation,
        Duration: duration,
        IsKeyFrame: isKeyFrame);

      presentation += duration;
      offset += checked((int)size);
    }
  }

  private static bool _CarriesWholeFramePicture(ReadOnlySpan<byte> subChunks) {
    var at = 0;
    while (at + 6 <= subChunks.Length) {
      var size = BinaryPrimitives.ReadUInt32LittleEndian(subChunks[at..]);
      var type = BinaryPrimitives.ReadUInt16LittleEndian(subChunks[(at + 4)..]);
      if (type is FliChunkType.BLACK or FliChunkType.BRUN or FliChunkType.COPY
          or FliChunkType.DTA_BRUN or FliChunkType.DTA_COPY)
        return true;

      if (size < 6 || size > subChunks.Length - at)
        return false;

      at += checked((int)size);
    }

    return false;
  }
}
