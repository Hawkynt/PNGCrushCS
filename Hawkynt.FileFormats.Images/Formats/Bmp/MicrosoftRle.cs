using System;
using System.IO;

namespace FileFormat.Bmp;

/// <summary>
/// Microsoft's run-length coding for palettised pictures — <c>BI_RLE8</c> and <c>BI_RLE4</c> — as one
/// walk over its opcodes.
/// </summary>
/// <remarks>
/// One walk and two callers. A run-length bitmap and an <c>MRLE</c> video frame are not similar
/// codings, they are the same coding: a count and a colour, a run of colours written out one after
/// another, and three escapes that move the pen instead of painting with it. What differs between
/// the two is not a single opcode but what the pen draws on. A bitmap starts on an empty canvas. A
/// video frame starts on the frame before it, and the escapes are precisely how it says "this part
/// of the picture did not change" — which is why the same delta that is a curiosity in a still is
/// the whole of the inter-frame coding in a film.
/// <para/>
/// So the canvas belongs to the caller. This writes one byte per pixel — a palette index, whatever
/// depth the indices were packed at in the stream — into <paramref name="canvas"/> at
/// <c>row * width + column</c>, and leaves every pixel no opcode names exactly as it found it.
/// <para/>
/// Row zero is the first row the coding names, and not the top of the picture. Which row of the
/// picture that is comes out of the header — a bitmap's rows usually run bottom-up — and turning
/// them the right way up is that reader's job. Doing it here as well would flip half the callers
/// twice.
/// </remarks>
internal static class MicrosoftRle {

  /// <summary>A count of zero is not a run of nothing; it introduces one of the three escapes.</summary>
  private const byte _ESCAPE = 0x00;

  private const byte _END_OF_LINE = 0x00;
  private const byte _END_OF_BITMAP = 0x01;
  private const byte _DELTA = 0x02;

  /// <summary>
  /// Walks one run-length coded picture onto a canvas.
  /// </summary>
  /// <param name="data">The coded bytes, starting at the first opcode.</param>
  /// <param name="canvas">One byte per pixel, <c>row * width + column</c>, which this paints onto.</param>
  /// <param name="width">The picture's width in pixels.</param>
  /// <param name="height">The picture's height in rows.</param>
  /// <param name="bitsPerPixel">4 or 8 — how many indices a colour byte carries.</param>
  /// <param name="refuseMalformed">
  /// Whether coding that does not fit the picture is an error. A decoder that has to be right says
  /// yes and gets an exception naming the opcode and where it was; a reader recovering whatever it
  /// can from a damaged still says no and gets the part that decoded.
  /// </param>
  /// <exception cref="InvalidDataException">
  /// Under <paramref name="refuseMalformed"/>, when an opcode runs off the picture or off the end of
  /// the data.
  /// </exception>
  internal static void Decode(
    ReadOnlySpan<byte> data,
    Span<byte> canvas,
    int width,
    int height,
    int bitsPerPixel,
    bool refuseMalformed) {
    if (bitsPerPixel is not (4 or 8))
      throw new ArgumentOutOfRangeException(
        nameof(bitsPerPixel), bitsPerPixel, "Microsoft run-length coding is defined at four bits a pixel and at eight.");

    var at = 0;
    var x = 0;
    var y = 0;

    while (true) {
      // Two bytes is the shortest opcode there is, so fewer than two left is the end of the walk. A
      // stream that stops there without having said so is common enough — the end-of-bitmap escape is
      // conventional rather than required — but a single byte left over is half an opcode, and that is
      // a damaged stream rather than a finished one.
      if (at >= data.Length)
        return;

      if (at + 1 >= data.Length) {
        if (refuseMalformed)
          throw new InvalidDataException(
            $"Run-length data ends on a single byte at offset {at}, where every opcode is at least two bytes long.");

        return;
      }

      var count = data[at++];
      var value = data[at++];

      if (count != _ESCAPE) {
        _WriteRun(canvas, width, height, bitsPerPixel, refuseMalformed, count, value, ref x, ref y, at - 2);
        continue;
      }

      switch (value) {
        case _END_OF_LINE:
          x = 0;
          ++y;
          continue;

        case _END_OF_BITMAP:
          return;

        case _DELTA:
          if (at + 1 >= data.Length) {
            if (refuseMalformed)
              throw new InvalidDataException(
                $"A run-length delta escape at offset {at - 2} is followed by {data.Length - at} byte(s) where it takes two.");

            return;
          }

          x += data[at++];
          y += data[at++];
          if (!refuseMalformed || (x <= width && y <= height))
            continue;

          throw new InvalidDataException(
            $"A run-length delta escape at offset {at - 4} moves to column {x} of row {y}, which is outside a "
            + $"{width}x{height} picture.");

        default:
          _WriteAbsolute(data, canvas, width, height, bitsPerPixel, refuseMalformed, value, ref at, ref x, ref y);
          continue;
      }
    }
  }

  /// <summary>Writes an encoded run: one colour byte repeated, or its two nibbles in turn.</summary>
  private static void _WriteRun(
    Span<byte> canvas,
    int width,
    int height,
    int bitsPerPixel,
    bool refuseMalformed,
    int count,
    byte value,
    ref int x,
    ref int y,
    int offset) {
    if (refuseMalformed && (y >= height || x + count > width))
      throw new InvalidDataException(
        $"A run-length run of {count} pixel(s) at offset {offset} starts at column {x} of row {y} and does not fit a "
        + $"{width}x{height} picture. Runs do not cross the end of a row.");

    var high = (byte)(value >> 4);
    var low = (byte)(value & 0x0F);

    for (var written = 0; written < count; ++written, ++x) {
      // Only reachable under a lenient walk, where a run may start off the picture and the part of it
      // that lands on the picture is still worth having. Neither coordinate can be negative: both
      // start at zero and every opcode that moves them adds an unsigned byte.
      if (y >= height || x >= width)
        continue;

      canvas[y * width + x] = bitsPerPixel == 8 ? value : (written & 1) == 0 ? high : low;
    }
  }

  /// <summary>
  /// Writes an absolute run: the pixels are spelled out one after another rather than repeated.
  /// </summary>
  /// <remarks>
  /// The bytes an absolute run occupies are padded out to a whole word, not merely to a whole byte.
  /// At four bits a pixel that is two roundings and not one — nibbles to a byte, then bytes to a
  /// word — and a reader that applied only the first is left half a byte out of step for the rest of
  /// the picture.
  /// </remarks>
  private static void _WriteAbsolute(
    ReadOnlySpan<byte> data,
    Span<byte> canvas,
    int width,
    int height,
    int bitsPerPixel,
    bool refuseMalformed,
    int count,
    ref int at,
    ref int x,
    ref int y) {
    var start = at;
    var bytes = bitsPerPixel == 8 ? count : (count + 1) / 2;
    var padded = bytes + (bytes & 1);

    if (start + bytes > data.Length) {
      if (refuseMalformed)
        throw new InvalidDataException(
          $"A run-length absolute run of {count} pixel(s) at offset {start - 2} needs {bytes} byte(s) and only "
          + $"{data.Length - start} remain.");

      at = data.Length;
      return;
    }

    if (refuseMalformed && (y >= height || x + count > width))
      throw new InvalidDataException(
        $"A run-length absolute run of {count} pixel(s) at offset {start - 2} starts at column {x} of row {y} and does "
        + $"not fit a {width}x{height} picture. Runs do not cross the end of a row.");

    for (var written = 0; written < count; ++written, ++x) {
      // Only reachable under a lenient walk, where a run may start off the picture and the part of it
      // that lands on the picture is still worth having. Neither coordinate can be negative: both
      // start at zero and every opcode that moves them adds an unsigned byte.
      if (y >= height || x >= width)
        continue;

      var source = data[start + (bitsPerPixel == 8 ? written : written / 2)];
      canvas[y * width + x] =
        bitsPerPixel == 8 ? source : (written & 1) == 0 ? (byte)(source >> 4) : (byte)(source & 0x0F);
    }

    // Past the padding whether or not every pixel of the run landed on the picture. Stopping at the
    // last one that did is what leaves the reader reading pixels as opcodes for the rest of the file.
    at = Math.Min(data.Length, start + padded);
  }
}
