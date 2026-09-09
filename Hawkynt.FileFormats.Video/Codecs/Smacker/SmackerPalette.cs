using System;
using System.IO;

namespace FileFormat.Codecs.Smacker;

/// <summary>
/// Builds a 256-colour palette from a Smacker palette chunk and the palette the picture before it used
/// — a chunk states only what changed, never the 256 colours whole.
/// </summary>
/// <remarks>
/// Three block shapes, told apart by the top two bits of a block's first byte: a run of entries kept as
/// they were, a run copied from somewhere else in the <i>previous</i> palette, and one new colour given
/// as six bits a component. Both copying shapes read the previous palette rather than the one being
/// built, so a copy can move colours about without a later block seeing a value an earlier one had
/// already written.
/// </remarks>
internal static class SmackerPalette {

  internal const int ENTRY_COUNT = 256;
  internal const int BYTE_COUNT = ENTRY_COUNT * 3;

  /// <summary>The six-bit to eight-bit component ladder, copied exactly. It is not <c>c * 255 / 63</c>
  /// and not <c>c &lt;&lt; 2 | c &gt;&gt; 4</c>: the first sixteen steps rise by four, then every
  /// sixteenth step rises by five instead, so a value worked out by either formula is a different
  /// palette on nearly half the entries.</summary>
  private static readonly byte[] _SIX_TO_EIGHT_BITS = [
    0x00, 0x04, 0x08, 0x0C, 0x10, 0x14, 0x18, 0x1C,
    0x20, 0x24, 0x28, 0x2C, 0x30, 0x34, 0x38, 0x3C,
    0x41, 0x45, 0x49, 0x4D, 0x51, 0x55, 0x59, 0x5D,
    0x61, 0x65, 0x69, 0x6D, 0x71, 0x75, 0x79, 0x7D,
    0x82, 0x86, 0x8A, 0x8E, 0x92, 0x96, 0x9A, 0x9E,
    0xA2, 0xA6, 0xAA, 0xAE, 0xB2, 0xB6, 0xBA, 0xBE,
    0xC3, 0xC7, 0xCB, 0xCF, 0xD3, 0xD7, 0xDB, 0xDF,
    0xE3, 0xE7, 0xEB, 0xEF, 0xF3, 0xF7, 0xFB, 0xFF,
  ];

  /// <summary>Builds a fresh 256-entry RGB palette from a chunk's own blocks and the palette in force
  /// before it, which is all black where there was none.</summary>
  /// <param name="chunk">The whole chunk, its own length byte included.</param>
  /// <param name="previous">The 768 bytes the picture before this one used, or <c>null</c> for the
  /// first picture in a file.</param>
  internal static byte[] Apply(ReadOnlySpan<byte> chunk, byte[]? previous) {
    var source = previous ?? new byte[BYTE_COUNT];
    var result = new byte[BYTE_COUNT];
    var blocks = chunk[1..];
    var entry = 0;
    var at = 0;

    while (entry < ENTRY_COUNT) {
      if (at >= blocks.Length)
        throw new InvalidDataException(
          $"A Smacker palette chunk ran out of blocks after {entry} of its {ENTRY_COUNT} colours were "
          + "formed.");

      var first = blocks[at++];
      if ((first & 0x80) != 0) {
        // 1ccccccc: leave the next c + 1 entries as the picture before this one had them.
        var count = Math.Min((first & 0x7F) + 1, ENTRY_COUNT - entry);
        _Copy(source, entry, result, entry, count);
        entry += count;
        continue;
      }

      if ((first & 0x40) != 0) {
        // 01cccccc, ssssssss: take c + 1 entries of the previous palette, starting at entry s.
        if (at >= blocks.Length)
          throw new InvalidDataException("A Smacker palette chunk's copy block has no source-index byte.");

        var length = (first & 0x3F) + 1;
        var start = blocks[at++];
        if (start + length > ENTRY_COUNT)
          throw new InvalidDataException(
            $"A Smacker palette chunk's copy block takes {length} entries from entry {start}, which "
            + $"reaches past the palette's own {ENTRY_COUNT}.");

        var count = Math.Min(length, ENTRY_COUNT - entry);
        _Copy(source, start, result, entry, count);
        entry += count;
        continue;
      }

      // 00rrrrrr, 00gggggg, 00bbbbbb: one new colour, six bits a component.
      if (at + 1 >= blocks.Length)
        throw new InvalidDataException("A Smacker palette chunk's new-colour block is short of its three bytes.");

      result[entry * 3] = _SIX_TO_EIGHT_BITS[first];
      result[entry * 3 + 1] = _SIX_TO_EIGHT_BITS[blocks[at] & 0x3F];
      result[entry * 3 + 2] = _SIX_TO_EIGHT_BITS[blocks[at + 1] & 0x3F];
      at += 2;
      ++entry;
    }

    return result;
  }

  private static void _Copy(byte[] source, int sourceEntry, byte[] destination, int destinationEntry, int count)
    => source.AsSpan(sourceEntry * 3, count * 3).CopyTo(destination.AsSpan(destinationEntry * 3));
}
