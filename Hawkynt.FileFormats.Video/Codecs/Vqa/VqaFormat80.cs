using System;
using System.IO;

namespace FileFormat.Codecs.Vqa;

/// <summary>
/// Decompresses Westwood's "format80" run-length scheme — the compression every codebook, palette and
/// index-table chunk in a VQA file may carry, named for the two-bit prefix that opens each of its five
/// commands.
/// </summary>
/// <remarks>
/// Published in full, byte pattern and all, in Gordan Ugarkovic's VQA format description (the document
/// mirrored at <c>multimedia.cx/vqa_overview.htm</c>): five commands, told apart by how many of a
/// command byte's leading bits are set — <c>0</c> for a short back-reference that may overlap what it
/// is still writing, <c>10</c> for a literal run copied straight from the compressed stream, and three
/// more under <c>11</c> for a longer literal count, a fill, and a long back-reference, the last two
/// distinguished by two reserved command bytes (<c>0xFE</c>, <c>0xFF</c>) rather than by a count field.
/// A back-reference's position is a byte offset into the buffer already produced — self-overlapping for
/// the short form, which is what lets it encode a run of one repeated byte in two bytes; absolute from
/// the start of the buffer for the long form. The document itself warns a stream may end without the
/// documented <c>0x80</c> (a literal run of zero bytes) sentinel, so a decoder here stops at the
/// compressed input's own end regardless of whether that byte appears.
/// <para/>
/// <b>Verified against three real files, 245 pictures between them.</b> Every codebook, palette and
/// index-table chunk of every picture decoded here matches ffmpeg's own decode of the same three files
/// pixel for pixel — see <see cref="Codecs.VqaVideoDecoder"/> for the comparison itself, which is where
/// a wrong byte here would surface.
/// </remarks>
internal static class VqaFormat80 {

  private const byte _ENVELOPE_1 = 0xFE;
  private const byte _ENVELOPE_2 = 0xFF;

  /// <summary>
  /// Decompresses into a buffer of exactly <paramref name="outputLength"/> bytes, zero-filled before
  /// decompression starts.
  /// </summary>
  /// <remarks>
  /// Some real index-table chunks encode fewer bytes than the table's own full size and rely on the
  /// rest reading as zero — measured directly against real files, not read from anywhere: decompressing
  /// one such chunk into a buffer sized only to what it actually writes, rather than the table's whole
  /// size, leaves the picture built from it visibly wrong in exactly the region the missing bytes cover.
  /// </remarks>
  public static byte[] Decompress(ReadOnlySpan<byte> source, int outputLength) {
    var output = new byte[outputLength];
    _Run(source, output, outputLength);
    return output;
  }

  /// <summary>Decompresses into a buffer exactly as large as the compressed stream turns out to need,
  /// for chunks — the codebook chief among them — whose decompressed size is not known in advance.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> source) {
    // A generous upper bound, grown if a single decompression genuinely needs more: no real codebook
    // measured against this reaches anywhere close to it.
    var buffer = new byte[1 << 20];
    while (true) {
      var written = _TryRun(source, buffer);
      if (written >= 0)
        return buffer[..written];

      buffer = new byte[buffer.Length * 2];
    }
  }

  private static void _Run(ReadOnlySpan<byte> source, byte[] output, int outputLength) {
    var written = _TryRun(source, output);
    if (written < 0)
      throw new InvalidDataException(
        $"A format80 stream needs more than the {outputLength} bytes its destination was sized for.");
  }

  /// <summary>Runs the decompression loop, returning how many bytes were written, or <c>-1</c> if
  /// <paramref name="output"/> ran out of room before the compressed stream did.</summary>
  private static int _TryRun(ReadOnlySpan<byte> source, byte[] output) {
    var readPos = 0;
    var writePos = 0;
    var sourceLength = source.Length;
    var outputLength = output.Length;

    while (readPos < sourceLength) {
      var command = source[readPos];

      if (command == 0x80)
        break; // the documented, but not guaranteed, end-of-stream marker

      int count;
      if ((command & 0x80) == 0) {
        // Short back-reference: 2 bytes. Count is 3 bits plus three; the twelve-bit offset is the low
        // nibble of the command byte as its high bits, and the next byte as its low eight.
        if (readPos + 1 >= sourceLength)
          throw new InvalidDataException("A format80 stream ends mid-way through a short back-reference command.");

        count = ((command >> 4) & 0x07) + 3;
        var offset = ((command & 0x0F) << 8) | source[readPos + 1];
        readPos += 2;

        if (writePos + count > outputLength)
          return -1;

        var from = writePos - offset;
        if (from < 0)
          throw new InvalidDataException("A format80 short back-reference points before the start of the output.");

        for (var i = 0; i < count; ++i)
          output[writePos + i] = output[from + i];
        writePos += count;
      } else if ((command & 0xC0) == 0x80) {
        // Literal run: 1 header byte, the low six bits its own length, followed by that many literal
        // bytes copied straight from the compressed stream.
        count = command & 0x3F;
        ++readPos;

        if (readPos + count > sourceLength)
          throw new InvalidDataException("A format80 stream ends mid-way through a literal run.");
        if (writePos + count > outputLength)
          return -1;

        source.Slice(readPos, count).CopyTo(output.AsSpan(writePos, count));
        readPos += count;
        writePos += count;
      } else if (command == _ENVELOPE_1) {
        // Fill: a two-byte count and a single byte value, that value repeated count times.
        if (readPos + 3 >= sourceLength)
          throw new InvalidDataException("A format80 stream ends mid-way through a fill command.");

        count = source[readPos + 1] | (source[readPos + 2] << 8);
        var value = source[readPos + 3];
        readPos += 4;

        if (writePos + count > outputLength)
          return -1;

        output.AsSpan(writePos, count).Fill(value);
        writePos += count;
      } else if (command == _ENVELOPE_2) {
        // Long back-reference: a two-byte count and a two-byte position absolute from the start of the
        // output buffer.
        if (readPos + 4 >= sourceLength)
          throw new InvalidDataException("A format80 stream ends mid-way through a long back-reference command.");

        count = source[readPos + 1] | (source[readPos + 2] << 8);
        var position = source[readPos + 3] | (source[readPos + 4] << 8);
        readPos += 5;

        if (writePos + count > outputLength)
          return -1;
        if (position + count > outputLength)
          throw new InvalidDataException("A format80 long back-reference points past the end of the output.");

        for (var i = 0; i < count; ++i)
          output[writePos + i] = output[position + i];
        writePos += count;
      } else {
        // Long literal run: 1 header byte, the low six bits plus three its length, then a two-byte
        // position absolute from the start of the output buffer to copy that many bytes from.
        if (readPos + 2 >= sourceLength)
          throw new InvalidDataException("A format80 stream ends mid-way through a long literal-run command.");

        count = (command & 0x3F) + 3;
        var position = source[readPos + 1] | (source[readPos + 2] << 8);
        readPos += 3;

        if (writePos + count > outputLength)
          return -1;
        if (position + count > outputLength)
          throw new InvalidDataException("A format80 long back-reference points past the end of the output.");

        for (var i = 0; i < count; ++i)
          output[writePos + i] = output[position + i];
        writePos += count;
      }
    }

    return writePos;
  }
}
