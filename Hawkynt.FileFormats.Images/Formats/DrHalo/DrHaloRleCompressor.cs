using System;
using System.IO;

namespace FileFormat.DrHalo;

/// <summary>Dr. Halo CUT per-scanline run-length coding.</summary>
/// <remarks>
/// A command byte governs what follows it: a count of zero ends the row, a byte with its top bit set
/// repeats the next byte that many times over, and one without it introduces that many literals.
/// Both forms count in the low seven bits, so a run and a literal block are each at most 127 long.
/// <para/>
/// This used to write every packet as a plain count followed by a value — the run form without its
/// top bit, which is the literal form. A reader following it takes the value byte as the first of a
/// literal block and everything after it shifts, so the row comes out neither the right length nor
/// the right content. Both reference decoders refused the result outright.
/// </remarks>
internal static class DrHaloRleCompressor {

  /// <summary>Marks a packet as a repeat rather than a block of literals.</summary>
  private const byte _RunFlag = 0x80;

  /// <summary>The most either form can cover, the count having seven bits.</summary>
  private const int _MaxRun = 0x7F;

  public static byte[] CompressScanline(ReadOnlySpan<byte> row) {
    if (row.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < row.Length) {
      var value = row[i];
      var run = 1;
      while (i + run < row.Length && run < _MaxRun && row[i + run] == value)
        ++run;

      if (run >= 2) {
        ms.WriteByte((byte)(_RunFlag | run));
        ms.WriteByte(value);
        i += run;
        continue;
      }

      // A lone pixel costs two bytes as a run and one as a literal, so lone pixels are gathered into
      // a block until a run worth coding begins.
      var start = i;
      while (i < row.Length && i - start < _MaxRun) {
        var ahead = 1;
        while (i + ahead < row.Length && ahead < 2 && row[i + ahead] == row[i])
          ++ahead;

        if (ahead >= 2)
          break;

        ++i;
      }

      ms.WriteByte((byte)(i - start));
      for (var j = start; j < i; ++j)
        ms.WriteByte(row[j]);
    }

    // Nothing follows the last packet, so the row states its own end.
    ms.WriteByte(0);
    return ms.ToArray();
  }

  public static byte[] DecompressScanline(ReadOnlySpan<byte> data, int width) {
    if (width <= 0)
      throw new InvalidDataException("Dr. Halo scanline width must be positive.");

    var output = new byte[width];
    var at = 0;
    var written = 0;

    while (at < data.Length) {
      var command = data[at++];
      var count = command & _MaxRun;
      if (count == 0) {
        if (written != width)
          throw new InvalidDataException($"Dr. Halo scanline ended after {written} of {width} pixels.");
        if (at != data.Length)
          throw new InvalidDataException("Unexpected bytes after the Dr. Halo scanline terminator.");
        return output;
      }

      if (written + count > width)
        throw new InvalidDataException("Dr. Halo RLE packet overruns the declared scanline width.");

      if ((command & _RunFlag) != 0) {
        if (at >= data.Length)
          throw new InvalidDataException("Truncated Dr. Halo repeated-value packet.");

        output.AsSpan(written, count).Fill(data[at++]);
        written += count;
        continue;
      }

      if (at + count > data.Length)
        throw new InvalidDataException("Truncated Dr. Halo literal packet.");

      data.Slice(at, count).CopyTo(output.AsSpan(written, count));
      at += count;
      written += count;
    }

    throw new InvalidDataException("Dr. Halo scanline is missing its end marker.");
  }
}
