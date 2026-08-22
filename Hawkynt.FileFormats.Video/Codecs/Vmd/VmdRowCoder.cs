using System;
using System.IO;

namespace FileFormat.Codecs.Vmd;

/// <summary>
/// Paints a video frame's rectangle onto the persistent canvas VMD keeps between pictures, in either
/// of the two rendering methods this decoder reads.
/// </summary>
/// <remarks>
/// A skip run needs no second buffer the way Interplay MVE's or id RoQ's own skip opcodes do — see
/// either decoder's own remarks for why those reach back across pictures. Here "copy the same position
/// from the previous frame" and "leave this byte alone" are the same instruction: nothing in a picture
/// still being painted has touched a skipped byte yet, in the left-to-right, top-to-bottom order both
/// methods paint in, so the canvas already holds exactly what a skip states. Method 1 is therefore
/// written as a genuine in-place mutation of one persistent buffer rather than a read from one buffer
/// into another.
/// </remarks>
internal static class VmdRowCoder {

  private const byte _LITERAL_FLAG = 0x80;
  private const byte _RUN_LENGTH_MASK = 0x7F;

  /// <summary>
  /// Method 1: one control byte a run, read left to right along each row of the rectangle and never
  /// reset between rows. A run with its top bit set is a literal — the run length is that byte's low
  /// seven bits plus one, and that many bytes follow in the stream to paint. A run with the top bit
  /// clear is a skip of the low seven bits plus one bytes, which needs nothing pulled from anywhere
  /// else — see this type's own remarks.
  /// </summary>
  internal static void DecodeMethod1(ReadOnlySpan<byte> data, byte[] canvas, int canvasWidth, int canvasHeight, int left, int top, int width, int height) {
    _ValidateRectangle(canvasWidth, canvasHeight, left, top, width, height);

    var position = 0;
    for (var row = 0; row < height; ++row) {
      var rowStart = (top + row) * canvasWidth + left;
      var offset = 0;

      while (offset < width) {
        if (position >= data.Length)
          throw new InvalidDataException(
            $"A method 1 VMD video frame ran out of row data at row {row}, column {offset} of a {width}x{height} rectangle.");

        var control = data[position++];

        if ((control & _LITERAL_FLAG) != 0) {
          var runLength = (control & _RUN_LENGTH_MASK) + 1;
          if (offset + runLength > width)
            throw new InvalidDataException(
              $"A method 1 VMD video frame's literal run at row {row}, column {offset} is {runLength} bytes, "
              + $"which runs past the rectangle's own width of {width}.");
          if (position + runLength > data.Length)
            throw new InvalidDataException(
              $"A method 1 VMD video frame's literal run at row {row}, column {offset} wants {runLength} bytes, "
              + "more than the packet holds.");

          data.Slice(position, runLength).CopyTo(canvas.AsSpan(rowStart + offset, runLength));
          position += runLength;
          offset += runLength;
        } else {
          var skipLength = control + 1;
          if (offset + skipLength > width)
            throw new InvalidDataException(
              $"A method 1 VMD video frame's skip run at row {row}, column {offset} is {skipLength} bytes, "
              + $"which runs past the rectangle's own width of {width}.");

          // Nothing to copy — see this type's own remarks on why a skip is a no-op here.
          offset += skipLength;
        }
      }
    }
  }

  /// <summary>Method 2: the rectangle's bytes in plain row-major order, one a pixel, and nothing else.</summary>
  internal static void DecodeMethod2(ReadOnlySpan<byte> data, byte[] canvas, int canvasWidth, int canvasHeight, int left, int top, int width, int height) {
    _ValidateRectangle(canvasWidth, canvasHeight, left, top, width, height);

    var required = (long)width * height;
    if (data.Length < required)
      throw new InvalidDataException(
        $"A method 2 VMD video frame is {data.Length} bytes, short of the {required} a {width}x{height} rectangle needs.");

    for (var row = 0; row < height; ++row) {
      var rowStart = (top + row) * canvasWidth + left;
      data.Slice(row * width, width).CopyTo(canvas.AsSpan(rowStart, width));
    }
  }

  private static void _ValidateRectangle(int canvasWidth, int canvasHeight, int left, int top, int width, int height) {
    if (left < 0 || top < 0 || width <= 0 || height <= 0 || left + width > canvasWidth || top + height > canvasHeight)
      throw new InvalidDataException(
        $"A VMD video frame states a rectangle of {width}x{height} at ({left},{top}), which does not fit "
        + $"inside the {canvasWidth}x{canvasHeight} picture.");
  }
}
