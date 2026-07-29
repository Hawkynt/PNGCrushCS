using System;
using System.IO;

namespace FileFormat.PublicPainter;

/// <summary>Compressor/decompressor for Public Painter's escape-byte RLE scheme.</summary>
/// <remarks>
/// The first byte of the file names an escape value. In the stream, any byte other than the escape
/// stands for itself; the escape introduces a run, followed by a repeat count minus one and the
/// byte to repeat. A literal that happens to equal the escape therefore has to be written as a
/// one-long run.
/// </remarks>
internal static class PublicPainterCompressor {

  /// <summary>Longest run a single command can express — the count is stored minus one.</summary>
  private const int _MAX_RUN = 256;

  /// <summary>Runs shorter than this cost more to encode than to write out literally.</summary>
  private const int _MIN_RUN = 3;

  /// <summary>Decompresses a Public Painter stream to the expected output size.</summary>
  /// <param name="compressed">The stream, starting at the first command byte.</param>
  /// <param name="escape">The escape value declared by the file header.</param>
  /// <param name="expectedSize">Number of bytes to produce.</param>
  public static byte[] Decompress(ReadOnlySpan<byte> compressed, byte escape, int expectedSize) {
    var output = new byte[expectedSize];
    var source = 0;
    var destination = 0;

    while (destination < expectedSize && source < compressed.Length) {
      var value = compressed[source++];
      var count = 1;

      if (value == escape) {
        if (source + 1 >= compressed.Length)
          break;

        count = compressed[source++] + 1;
        value = compressed[source++];
      }

      var end = Math.Min(destination + count, expectedSize);
      while (destination < end)
        output[destination++] = value;
    }

    return output;
  }

  /// <summary>Picks an escape value, preferring one absent from the data so no literal needs escaping.</summary>
  public static byte ChooseEscape(ReadOnlySpan<byte> data) {
    Span<int> counts = stackalloc int[256];
    foreach (var b in data)
      ++counts[b];

    var best = 0;
    for (var candidate = 0; candidate < counts.Length; ++candidate) {
      if (counts[candidate] == 0)
        return (byte)candidate;

      if (counts[candidate] < counts[best])
        best = candidate;
    }

    return (byte)best;
  }

  /// <summary>Compresses data using the escape-byte scheme.</summary>
  public static byte[] Compress(ReadOnlySpan<byte> data, byte escape) {
    using var ms = new MemoryStream();

    var position = 0;
    while (position < data.Length) {
      var value = data[position];

      var run = 1;
      while (run < _MAX_RUN && position + run < data.Length && data[position + run] == value)
        ++run;

      // The escape value can never be written literally, however short the run.
      if (run >= _MIN_RUN || value == escape) {
        ms.WriteByte(escape);
        ms.WriteByte((byte)(run - 1));
        ms.WriteByte(value);
      } else
        for (var i = 0; i < run; ++i)
          ms.WriteByte(value);

      position += run;
    }

    return ms.ToArray();
  }
}
