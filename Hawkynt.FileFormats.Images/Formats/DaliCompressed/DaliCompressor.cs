using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.DaliCompressed;

/// <summary>Compressor and decompressor for Dali's packed screen format.</summary>
/// <remarks>
/// The stream contains one count byte and one four-byte value per run. Counts 1..255 mean that many
/// screen groups; zero means 256, matching the original unsigned-byte decoder's wraparound behavior.
/// Groups are visited vertically down the ST screen before moving four bytes to the right.
/// </remarks>
internal static class DaliCompressor {

  /// <summary>Bytes in one compressed screen group.</summary>
  public const int GroupSize = 4;

  /// <summary>Bytes per scanline of an ST screen.</summary>
  public const int BytesPerRow = 160;

  /// <summary>Size of an uncompressed ST screen.</summary>
  public const int ScreenSize = 32_000;

  /// <summary>Number of four-byte groups in one screen.</summary>
  public const int GroupCount = ScreenSize / GroupSize;

  /// <summary>Longest run one count byte represents; 256 is encoded as zero.</summary>
  private const int _MAX_RUN = 256;

  /// <summary>Enumerates screen offsets in the format's vertical traversal order.</summary>
  private static IEnumerable<int> _TraversalOrder() {
    for (var x = 0; x < BytesPerRow; x += GroupSize)
      for (var offset = x; offset < ScreenSize; offset += BytesPerRow)
        yield return offset;
  }

  /// <summary>Rebuilds exactly one 32,000-byte ST screen from parallel count/value tables.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values) {
    if (counts.Length is < 1 or > GroupCount)
      throw new InvalidDataException($"Compressed Dali count table must contain 1..{GroupCount} entries.");
    if (values.Length != checked(counts.Length * GroupSize))
      throw new InvalidDataException("Compressed Dali value table must contain exactly four bytes per count entry.");

    var screen = new byte[ScreenSize];
    using var offsets = _TraversalOrder().GetEnumerator();
    var expandedGroups = 0;

    for (var runIndex = 0; runIndex < counts.Length; ++runIndex) {
      var run = counts[runIndex] == 0 ? _MAX_RUN : counts[runIndex];
      if (expandedGroups > GroupCount - run)
        throw new InvalidDataException("Compressed Dali run table expands beyond the 32,000-byte screen.");

      var value = values.Slice(runIndex * GroupSize, GroupSize);
      for (var i = 0; i < run; ++i) {
        if (!offsets.MoveNext())
          throw new InvalidDataException("Compressed Dali run table expands beyond the screen traversal.");
        value.CopyTo(screen.AsSpan(offsets.Current, GroupSize));
      }

      expandedGroups += run;
    }

    if (expandedGroups != GroupCount)
      throw new InvalidDataException($"Compressed Dali run table expands to {expandedGroups} groups; exactly {GroupCount} are required.");

    return screen;
  }

  /// <summary>Splits exactly one ST screen into the format's parallel count and value tables.</summary>
  public static (byte[] Counts, byte[] Values) Compress(ReadOnlySpan<byte> screen) {
    if (screen.Length != ScreenSize)
      throw new ArgumentException($"Compressed Dali screen must contain exactly {ScreenSize} bytes.", nameof(screen));

    var offsets = new List<int>(GroupCount);
    offsets.AddRange(_TraversalOrder());
    var counts = new List<byte>();
    var values = new List<byte>();

    var index = 0;
    while (index < offsets.Count) {
      var start = offsets[index];
      var run = 1;
      while (run < _MAX_RUN && index + run < offsets.Count
             && screen.Slice(offsets[index + run], GroupSize).SequenceEqual(screen.Slice(start, GroupSize)))
        ++run;

      counts.Add(run == _MAX_RUN ? (byte)0 : (byte)run);
      for (var i = 0; i < GroupSize; ++i)
        values.Add(screen[start + i]);
      index += run;
    }

    return (counts.ToArray(), values.ToArray());
  }
}
