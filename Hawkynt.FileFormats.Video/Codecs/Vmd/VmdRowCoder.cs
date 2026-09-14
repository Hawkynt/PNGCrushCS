using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.Vmd;

/// <summary>Paints the three classic eight-bit Sierra VMD rectangle methods onto a persistent canvas.</summary>
/// <remarks>
/// Converted from FFmpeg's LGPL-2.1-or-later <c>libavcodec/vmdvideo.c</c>; provenance and licence are
/// recorded beside this file. Methods 1 and 3 are inter-frame codings: a control byte with its high bit
/// clear copies that run from the preceding picture. Method 3 adds a pair-oriented RLE spelling inside
/// literal runs. Method 2 is a plain row-major rectangle and needs no reference picture.
/// </remarks>
internal static class VmdRowCoder {

  private const byte _LITERAL_FLAG = 0x80;
  private const byte _RUN_LENGTH_MASK = 0x7F;
  private const byte _PAIR_RLE_MARKER = 0xFF;

  internal static void DecodeMethod1(
    ReadOnlySpan<byte> data, byte[] canvas, int canvasWidth, int canvasHeight,
    int left, int top, int width, int height, bool hasPreviousFrame) {
    _ValidateRectangle(canvasWidth, canvasHeight, left, top, width, height);

    var position = 0;
    for (var row = 0; row < height; ++row) {
      var rowStart = (top + row) * canvasWidth + left;
      var offset = 0;

      while (offset < width) {
        var control = _ReadByte(data, ref position, "method 1", row, offset, width, height);
        var runLength = (control & _RUN_LENGTH_MASK) + 1;
        if (offset + runLength > width)
          throw new InvalidDataException(
            $"A method 1 VMD run at row {row}, column {offset} is {runLength} bytes, "
            + $"past the rectangle width of {width}.");

        if ((control & _LITERAL_FLAG) != 0) {
          if (position + runLength > data.Length)
            throw new InvalidDataException(
              $"A method 1 VMD literal at row {row}, column {offset} wants {runLength} bytes, "
              + "more than the packet holds.");
          data.Slice(position, runLength).CopyTo(canvas.AsSpan(rowStart + offset, runLength));
          position += runLength;
        } else if (!hasPreviousFrame)
          throw new InvalidDataException(
            $"A method 1 VMD first picture asks to copy {runLength} pixels from a preceding picture that does not exist.");

        offset += runLength;
      }
    }
  }

  internal static void DecodeMethod2(
    ReadOnlySpan<byte> data, byte[] canvas, int canvasWidth, int canvasHeight,
    int left, int top, int width, int height) {
    _ValidateRectangle(canvasWidth, canvasHeight, left, top, width, height);

    var required = checked(width * height);
    if (data.Length < required)
      throw new InvalidDataException(
        $"A method 2 VMD rectangle is {data.Length} bytes, short of the {required} a {width}x{height} rectangle needs.");

    for (var row = 0; row < height; ++row)
      data.Slice(row * width, width).CopyTo(canvas.AsSpan((top + row) * canvasWidth + left, width));
  }

  internal static void DecodeMethod3(
    ReadOnlySpan<byte> data, byte[] canvas, int canvasWidth, int canvasHeight,
    int left, int top, int width, int height, bool hasPreviousFrame) {
    _ValidateRectangle(canvasWidth, canvasHeight, left, top, width, height);

    var position = 0;
    for (var row = 0; row < height; ++row) {
      var rowStart = (top + row) * canvasWidth + left;
      var offset = 0;

      while (offset < width) {
        var control = _ReadByte(data, ref position, "method 3", row, offset, width, height);
        var runLength = (control & _RUN_LENGTH_MASK) + 1;
        if (offset + runLength > width)
          throw new InvalidDataException(
            $"A method 3 VMD run at row {row}, column {offset} is {runLength} bytes, "
            + $"past the rectangle width of {width}.");

        if ((control & _LITERAL_FLAG) == 0) {
          if (!hasPreviousFrame)
            throw new InvalidDataException(
              $"A method 3 VMD first picture asks to copy {runLength} pixels from a preceding picture that does not exist.");
          offset += runLength;
          continue;
        }

        if (position >= data.Length)
          throw new InvalidDataException(
            $"A method 3 VMD literal at row {row}, column {offset} has no data behind its control byte.");

        if (data[position] != _PAIR_RLE_MARKER) {
          if (position + runLength > data.Length)
            throw new InvalidDataException(
              $"A method 3 VMD literal at row {row}, column {offset} wants {runLength} bytes, "
              + "more than the packet holds.");
          data.Slice(position, runLength).CopyTo(canvas.AsSpan(rowStart + offset, runLength));
          position += runLength;
          offset += runLength;
          continue;
        }

        ++position;
        _DecodePairRle(data, ref position, canvas.AsSpan(rowStart + offset, runLength));
        offset += runLength;
      }
    }
  }

  /// <summary>
  /// Expands method 3's inner RLE. An odd output begins with one literal byte; the remainder is made
  /// of two-byte units. High-bit commands copy an even literal byte count, low-bit commands repeat one
  /// little-endian two-byte value.
  /// </summary>
  private static void _DecodePairRle(ReadOnlySpan<byte> data, ref int position, Span<byte> destination) {
    var written = 0;
    if ((destination.Length & 1) != 0) {
      if (position >= data.Length)
        throw new InvalidDataException("A method 3 VMD pair-RLE run is missing its odd leading literal byte.");
      destination[written++] = data[position++];
    }

    while (written < destination.Length) {
      if (position >= data.Length)
        throw new InvalidDataException(
          $"A method 3 VMD pair-RLE run ended after {written} of {destination.Length} output bytes.");

      var command = data[position++];
      if ((command & _LITERAL_FLAG) != 0) {
        var count = (command & _RUN_LENGTH_MASK) * 2;
        if (count == 0)
          continue;
        if (written + count > destination.Length || position + count > data.Length)
          throw new InvalidDataException("A method 3 VMD pair-RLE literal runs past its declared literal span.");
        data.Slice(position, count).CopyTo(destination[written..]);
        position += count;
        written += count;
        continue;
      }

      var repetitions = command;
      var countBytes = repetitions * 2;
      if (countBytes == 0)
        continue;
      if (written + countBytes > destination.Length || position + 2 > data.Length)
        throw new InvalidDataException("A method 3 VMD pair-RLE repeat runs past its declared literal span.");

      var pair = BinaryPrimitives.ReadUInt16LittleEndian(data[position..]);
      position += 2;
      for (var i = 0; i < repetitions; ++i) {
        BinaryPrimitives.WriteUInt16LittleEndian(destination[written..], pair);
        written += 2;
      }
    }
  }

  private static byte _ReadByte(ReadOnlySpan<byte> data, ref int position, string method, int row, int offset, int width, int height) {
    if (position >= data.Length)
      throw new InvalidDataException(
        $"A {method} VMD rectangle ran out of row data at row {row}, column {offset} of {width}x{height}.");
    return data[position++];
  }

  private static void _ValidateRectangle(int canvasWidth, int canvasHeight, int left, int top, int width, int height) {
    if (left < 0 || top < 0 || width <= 0 || height <= 0
        || left > canvasWidth - width || top > canvasHeight - height)
      throw new InvalidDataException(
        $"A VMD video frame states a rectangle of {width}x{height} at ({left},{top}), which does not fit "
        + $"inside the {canvasWidth}x{canvasHeight} picture.");
  }
}
