using System;
using System.IO;

namespace FileFormat.Gif;

/// <summary>Colour-table sizing and emission.</summary>
/// <remarks>
/// GIF does not store a colour table's length. It stores a 3-bit exponent <c>e</c>, and the table is
/// then exactly <c>2^(e+1)</c> entries — a decoder reads that many triplets and then expects the next
/// block to start. A table of, say, three colours therefore cannot be written as nine bytes: the
/// smallest exponent that holds three entries is 1, so twelve bytes have to go out and the last
/// entry is padding. Emitting the short table instead shifts every following byte and the file is
/// unreadable from the image descriptor onwards.
/// </remarks>
internal static class GifColorTable {

  /// <summary>The smallest exponent <c>e</c> with <c>2^(e+1) &gt;= entries</c>, clamped to the 0..7
  /// the 3-bit field can hold.</summary>
  public static byte SizeExponent(int entries) {
    for (byte e = 0; e < 7; ++e)
      if (entries <= 1 << (e + 1))
        return e;
    return 7;
  }

  /// <summary>The entry count a table declared with exponent <paramref name="sizeExponent"/> holds.</summary>
  public static int EntryCount(byte sizeExponent) => 1 << ((sizeExponent & 0x07) + 1);

  /// <summary>Writes <paramref name="table"/> as exactly <c>2^(sizeExponent+1)</c> RGB triplets,
  /// zero-padding a short table and truncating an over-long one.</summary>
  public static void Write(Stream output, byte[] table, byte sizeExponent) {
    var needed = EntryCount(sizeExponent) * 3;
    var copy = Math.Min(table.Length, needed);
    if (copy > 0)
      output.Write(table, 0, copy);
    for (var i = copy; i < needed; ++i)
      output.WriteByte(0);
  }
}
