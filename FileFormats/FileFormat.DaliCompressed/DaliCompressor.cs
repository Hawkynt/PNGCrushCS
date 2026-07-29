using System;
using System.Collections.Generic;

namespace FileFormat.DaliCompressed;

/// <summary>Compressor and decompressor for Dali's packed screen format.</summary>
/// <remarks>
/// The scheme splits into two parallel streams: a run of counts and a run of four-byte values. A
/// count says how many consecutive positions repeat the value that follows it in the other stream,
/// which keeps the counts byte-aligned and the values word-aligned.
/// <para/>
/// The traversal is not raster order. The screen is walked four bytes at a time down each column
/// group before moving right — the ST stores four bitplanes interleaved per 16 pixels, so a
/// four-byte group is one screen chunk and a vertical run of them is what a flat-coloured area
/// actually produces.
/// </remarks>
internal static class DaliCompressor {

  /// <summary>Bytes in one screen chunk: four interleaved bitplane words' worth.</summary>
  public const int GroupSize = 4;

  /// <summary>Bytes per scanline of an ST screen.</summary>
  public const int BytesPerRow = 160;

  /// <summary>Size of an uncompressed screen.</summary>
  public const int ScreenSize = 32000;

  /// <summary>Longest run a single count byte can express.</summary>
  private const int _MAX_RUN = 255;

  /// <summary>Enumerates screen offsets in the order the format stores them.</summary>
  private static IEnumerable<int> _TraversalOrder() {
    for (var x = 0; x < BytesPerRow; x += GroupSize)
    for (var offset = x; offset < ScreenSize; offset += BytesPerRow)
      yield return offset;
  }

  /// <summary>Rebuilds a screen from the count and value streams.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values) {
    var screen = new byte[ScreenSize];
    int countIndex = 0, valueIndex = 0, remaining = 0;

    foreach (var offset in _TraversalOrder()) {
      if (--remaining <= 0) {
        if (countIndex >= counts.Length || valueIndex + GroupSize > values.Length)
          break;

        remaining = counts[countIndex++];
        if (remaining == 0)
          throw new System.IO.InvalidDataException("Dali run length of zero would never advance.");

        valueIndex += GroupSize;
      }

      values.Slice(valueIndex - GroupSize, GroupSize).CopyTo(screen.AsSpan(offset));
    }

    return screen;
  }

  /// <summary>Splits a screen into the count and value streams.</summary>
  public static (byte[] Counts, byte[] Values) Compress(ReadOnlySpan<byte> screen) {
    var counts = new List<byte>();
    var values = new List<byte>();

    var offsets = new List<int>(ScreenSize / GroupSize);
    offsets.AddRange(_TraversalOrder());

    var index = 0;
    while (index < offsets.Count) {
      var start = offsets[index];
      var run = 1;
      while (run < _MAX_RUN && index + run < offsets.Count
             && screen.Slice(offsets[index + run], GroupSize).SequenceEqual(screen.Slice(start, GroupSize)))
        ++run;

      counts.Add((byte)run);
      values.AddRange(screen.Slice(start, GroupSize).ToArray());
      index += run;
    }

    return (counts.ToArray(), values.ToArray());
  }
}
