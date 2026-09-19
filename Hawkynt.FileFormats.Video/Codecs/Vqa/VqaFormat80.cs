using System;
using System.IO;

namespace FileFormat.Codecs.Vqa;

/// <summary>
/// Westwood's "format80" byte-stream compression used by VQA codebooks, palettes and pointer tables.
/// </summary>
/// <remarks>
/// The original algorithm has five commands: literal copy, short relative copy, long absolute copy,
/// fill and very-long absolute copy. HiColor VQA changes only the two absolute-copy commands: their
/// positions become distances backwards from the current output position. Both forms are kept here so
/// the video codec does not need a second decompressor for that one semantic difference.
/// <para/>
/// <see cref="CompressLiterals"/> deliberately emits only the format's literal command. It is not a
/// compression heuristic; it is the smallest deterministic writer needed when a VQA syntax requires
/// a format80 envelope (notably VPTZ) and leaves optimisation to a future compressor without making
/// correctness depend on it.
/// </remarks>
internal static class VqaFormat80 {

  private const byte _ENVELOPE_1 = 0xFE;
  private const byte _ENVELOPE_2 = 0xFF;
  private const int _MAX_LITERAL = 0x3F;

  /// <summary>Decompresses into an exactly sized, initially zero-filled buffer.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> source, int outputLength) {
    var output = new byte[outputLength];
    _Run(source, output, outputLength, relativeLongReferences: false);
    return output;
  }

  /// <summary>Decompresses a normal format80 stream whose output size is not known beforehand.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> source)
    => _DecompressVariable(source, relativeLongReferences: false);

  /// <summary>
  /// Decompresses HiColor VQA's modified format80 form, where command 3 and command 5 positions are
  /// backward distances from the current output pointer instead of absolute destination positions.
  /// </summary>
  public static byte[] DecompressRelative(ReadOnlySpan<byte> source)
    => _DecompressVariable(source, relativeLongReferences: true);

  /// <summary>
  /// Produces a valid normal format80 stream using literal runs only. The final <c>0x80</c> is the
  /// documented zero-length literal/end marker.
  /// </summary>
  public static byte[] CompressLiterals(ReadOnlySpan<byte> source) {
    using var output = new MemoryStream(source.Length + source.Length / _MAX_LITERAL + 2);
    var at = 0;
    while (at < source.Length) {
      var count = Math.Min(_MAX_LITERAL, source.Length - at);
      output.WriteByte((byte)(0x80 | count));
      output.Write(source.Slice(at, count));
      at += count;
    }

    output.WriteByte(0x80);
    return output.ToArray();
  }

  private static byte[] _DecompressVariable(ReadOnlySpan<byte> source, bool relativeLongReferences) {
    var buffer = new byte[1 << 20];
    while (true) {
      var written = _TryRun(source, buffer, relativeLongReferences);
      if (written >= 0)
        return buffer[..written];

      buffer = new byte[checked(buffer.Length * 2)];
    }
  }

  private static void _Run(ReadOnlySpan<byte> source, byte[] output, int outputLength, bool relativeLongReferences) {
    var written = _TryRun(source, output, relativeLongReferences);
    if (written < 0)
      throw new InvalidDataException(
        $"A format80 stream needs more than the {outputLength} bytes its destination was sized for.");
  }

  /// <summary>Returns bytes written, or -1 when the supplied output buffer is too small.</summary>
  private static int _TryRun(ReadOnlySpan<byte> source, byte[] output, bool relativeLongReferences) {
    var readPos = 0;
    var writePos = 0;
    var sourceLength = source.Length;
    var outputLength = output.Length;

    while (readPos < sourceLength) {
      var command = source[readPos];

      if (command == 0x80)
        break;

      int count;
      if ((command & 0x80) == 0) {
        if (readPos + 1 >= sourceLength)
          throw new InvalidDataException("A format80 stream ends mid-way through a short back-reference command.");

        count = ((command >> 4) & 0x07) + 3;
        var distance = ((command & 0x0F) << 8) | source[readPos + 1];
        readPos += 2;

        if (writePos + count > outputLength)
          return -1;

        var from = writePos - distance;
        if (from < 0)
          throw new InvalidDataException("A format80 short back-reference points before the start of the output.");

        for (var i = 0; i < count; ++i)
          output[writePos + i] = output[from + i];
        writePos += count;
      } else if ((command & 0xC0) == 0x80) {
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
        if (readPos + 4 >= sourceLength)
          throw new InvalidDataException("A format80 stream ends mid-way through a long back-reference command.");

        count = source[readPos + 1] | (source[readPos + 2] << 8);
        var encodedPosition = source[readPos + 3] | (source[readPos + 4] << 8);
        readPos += 5;

        if (writePos + count > outputLength)
          return -1;

        var from = relativeLongReferences ? writePos - encodedPosition : encodedPosition;
        _ValidateLongCopy(from, count, writePos, outputLength, relativeLongReferences);
        for (var i = 0; i < count; ++i)
          output[writePos + i] = output[from + i];
        writePos += count;
      } else {
        if (readPos + 2 >= sourceLength)
          throw new InvalidDataException("A format80 stream ends mid-way through a long back-reference command.");

        count = (command & 0x3F) + 3;
        var encodedPosition = source[readPos + 1] | (source[readPos + 2] << 8);
        readPos += 3;

        if (writePos + count > outputLength)
          return -1;

        var from = relativeLongReferences ? writePos - encodedPosition : encodedPosition;
        _ValidateLongCopy(from, count, writePos, outputLength, relativeLongReferences);
        for (var i = 0; i < count; ++i)
          output[writePos + i] = output[from + i];
        writePos += count;
      }
    }

    return writePos;
  }

  private static void _ValidateLongCopy(int from, int count, int writePos, int outputLength, bool relative) {
    if (from < 0 || from + count > outputLength || relative && from >= writePos)
      throw new InvalidDataException(
        relative
          ? "A modified format80 back-reference points outside data already produced."
          : "A format80 long back-reference points past the destination buffer.");
  }
}
