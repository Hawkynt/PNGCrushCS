using System;
using System.IO;

namespace FileFormat.Codecs.Tscc;

/// <summary>
/// TSCC's run-length coding, walked onto a canvas the caller supplies — the same shape as
/// <c>MicrosoftRle</c> in the image package, and the same coding in every respect but two: a pixel
/// here is one, two, three or four whole bytes rather than a packed index, and the padding that keeps
/// an absolute run on a word boundary applies only at eight bits, where a nibble was never in play.
/// </summary>
/// <remarks>
/// Published as "Description of the TechSmith Screen Capture Codec (TSCC)" by Mike Melanson and
/// Konstantin Shishkov. A count and a colour, a run of colours written out one after another, and
/// three escapes that move the pen instead of painting with it — the same description Microsoft's own
/// run-length coding answers to, which is not a coincidence the source says anything about but is
/// exactly what comparing the two byte for byte shows.
/// <para/>
/// The canvas belongs to the caller for the reason it does in <c>MicrosoftRle</c>: an escape that
/// names no opcode for part of the picture is saying that part did not change, and a delta frame is
/// coded almost entirely out of such escapes. Row zero is the first row the coding names and not the
/// top of the picture — TSCC frames are coded bottom to top, and turning them right way up is the
/// decoder's job, not this walk's.
/// </remarks>
internal static class TsccRle {

  private const byte _ESCAPE = 0x00;
  private const byte _END_OF_LINE = 0x00;
  private const byte _END_OF_BITMAP = 0x01;
  private const byte _POSITION_CHANGE = 0x02;

  /// <summary>
  /// Walks one run-length coded picture onto a canvas.
  /// </summary>
  /// <param name="data">The decompressed bytes, starting at the first opcode.</param>
  /// <param name="canvas">One pixel of <paramref name="bytesPerPixel"/> bytes at
  /// <c>(row * width + column) * bytesPerPixel</c>, which this paints onto.</param>
  internal static void Decode(ReadOnlySpan<byte> data, Span<byte> canvas, int width, int height, int bytesPerPixel) {
    var at = 0;
    var x = 0;
    var y = 0;

    while (true) {
      if (at >= data.Length)
        return;

      var opcodeOffset = at;
      var count = data[at++];

      if (count == _ESCAPE) {
        if (at >= data.Length)
          throw new InvalidDataException(
            $"TSCC run-length data ends on a single byte at offset {opcodeOffset}, where every opcode is at least "
            + "two bytes long.");

        var value = data[at++];
        switch (value) {
          case _END_OF_LINE:
            x = 0;
            ++y;
            continue;

          case _END_OF_BITMAP:
            return;

          case _POSITION_CHANGE:
            if (at + 1 >= data.Length)
              throw new InvalidDataException(
                $"A TSCC position-change escape at offset {opcodeOffset} is followed by {data.Length - at} byte(s) "
                + "where it takes two.");

            x += data[at++];
            y += data[at++];
            if (x > width || y > height)
              throw new InvalidDataException(
                $"A TSCC position-change escape at offset {opcodeOffset} moves to column {x} of row {y}, which is "
                + $"outside a {width}x{height} picture.");

            continue;

          default:
            _WriteAbsolute(data, canvas, width, height, bytesPerPixel, value, opcodeOffset, ref at, ref x, ref y);
            continue;
        }
      }

      _WriteRun(data, canvas, width, height, bytesPerPixel, count, opcodeOffset, ref at, ref x, ref y);
    }
  }

  /// <summary>"b0 is not 0": a run of <paramref name="count"/> repeats of the one pixel that follows.</summary>
  private static void _WriteRun(
    ReadOnlySpan<byte> data, Span<byte> canvas, int width, int height, int bytesPerPixel, int count,
    int opcodeOffset, ref int at, ref int x, ref int y) {
    if (at + bytesPerPixel > data.Length)
      throw new InvalidDataException(
        $"A TSCC run of {count} pixel(s) at offset {opcodeOffset} names a {bytesPerPixel}-byte pixel and only "
        + $"{data.Length - at} byte(s) remain.");

    if (y >= height || x + count > width)
      throw new InvalidDataException(
        $"A TSCC run of {count} pixel(s) at offset {opcodeOffset} starts at column {x} of row {y} and does not fit "
        + $"a {width}x{height} picture. Runs do not cross the end of a row.");

    var pixel = data.Slice(at, bytesPerPixel);
    var stride = width * bytesPerPixel;

    for (var written = 0; written < count; ++written, ++x)
      pixel.CopyTo(canvas.Slice(y * stride + x * bytesPerPixel, bytesPerPixel));

    at += bytesPerPixel;
  }

  /// <summary>
  /// Writes an absolute run: the pixels are spelled out one after another rather than repeated.
  /// </summary>
  /// <remarks>
  /// Padding applies only at eight bits a pixel — "if the data is 8-bit and the copy length is odd,
  /// advance the stream pointer by 1 as it is padded (just like MS RLE)." At sixteen, twenty-four and
  /// thirty-two bits a pixel's own width already keeps every run a whole number of bytes, so nothing
  /// more is added.
  /// </remarks>
  private static void _WriteAbsolute(
    ReadOnlySpan<byte> data, Span<byte> canvas, int width, int height, int bytesPerPixel, int count,
    int opcodeOffset, ref int at, ref int x, ref int y) {
    var start = at;
    var bytes = count * bytesPerPixel;
    var padded = bytesPerPixel == 1 ? bytes + (bytes & 1) : bytes;

    if (start + bytes > data.Length)
      throw new InvalidDataException(
        $"A TSCC absolute run of {count} pixel(s) at offset {opcodeOffset} needs {bytes} byte(s) and only "
        + $"{data.Length - start} remain.");

    if (y >= height || x + count > width)
      throw new InvalidDataException(
        $"A TSCC absolute run of {count} pixel(s) at offset {opcodeOffset} starts at column {x} of row {y} and does "
        + $"not fit a {width}x{height} picture. Runs do not cross the end of a row.");

    var stride = width * bytesPerPixel;
    for (var written = 0; written < count; ++written, ++x) {
      var source = data.Slice(start + written * bytesPerPixel, bytesPerPixel);
      source.CopyTo(canvas.Slice(y * stride + x * bytesPerPixel, bytesPerPixel));
    }

    at = start + padded;
  }
}
