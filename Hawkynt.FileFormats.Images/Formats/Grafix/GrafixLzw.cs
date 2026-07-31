using System;
using System.IO;

namespace FileFormat.Grafix;

/// <summary>The dictionary coder a packed Grafix picture uses.</summary>
/// <remarks>
/// A variant of the usual dictionary scheme that stores where each entry's expansion begins rather
/// than the expansion itself, and reads the end from the entry after it. That works because entries
/// are only ever appended and each begins where the previous one's output ended, so consecutive
/// entries bound each other — the dictionary is a list of positions in what has already been
/// written, and needs no storage of its own.
/// <para/>
/// Codes start nine bits wide and one code widens them to ten; another resets the dictionary. The
/// entry a code is about to create is recorded before it is emitted, which is what lets a code
/// refer to itself — the case where a run repeats its own first byte.
/// </remarks>
internal static class GrafixLzw {

  /// <summary>Entries the dictionary holds before it must be reset.</summary>
  private const int _MAX_CODES = 1024;

  /// <summary>The first code that is not a literal byte.</summary>
  private const int _FIRST_CODE = 258;

  /// <summary>The code that widens the others to ten bits.</summary>
  private const int _WIDEN = 256;

  /// <summary>The code that empties the dictionary.</summary>
  private const int _RESET = 257;

  public static void Unpack(
    ReadOnlySpan<byte> packed, int offset, int end, Span<byte> unpacked, int unpackedOffset, int unpackedEnd) {
    var offsets = new int[_MAX_CODES];
    int bits = 0, bitsCount = 0, codes = _FIRST_CODE, codeBits = 9;

    while (unpackedOffset < unpackedEnd) {
      while (bitsCount < codeBits) {
        if (offset >= end)
          throw new InvalidDataException("A packed Grafix picture ends before its picture does.");

        bits |= packed[offset++] << bitsCount;
        bitsCount += 8;
      }

      var code = bits & ((1 << codeBits) - 1);
      bits >>= codeBits;
      bitsCount -= codeBits;

      switch (code) {
        case _WIDEN:
          if (codeBits == 10)
            throw new InvalidDataException("A packed Grafix picture widens its codes twice.");

          codeBits = 10;
          continue;

        case _RESET:
          codes = _FIRST_CODE;
          codeBits = 9;
          continue;
      }

      if (codes >= _MAX_CODES)
        throw new InvalidDataException("A packed Grafix picture overruns its dictionary.");

      // Recorded before the code is emitted, so a code may refer to the entry it is creating.
      offsets[codes] = unpackedOffset;

      if (code < 256)
        unpacked[unpackedOffset++] = (byte)code;
      else if (code >= codes)
        throw new InvalidDataException("A packed Grafix picture names a dictionary entry it has not made.");
      else {
        int source = offsets[code], last = offsets[code + 1];
        if (unpackedOffset + last - source >= unpackedEnd)
          throw new InvalidDataException("A packed Grafix picture's expansion runs past the picture.");

        do
          unpacked[unpackedOffset++] = unpacked[source++];
        while (source <= last);
      }

      ++codes;
    }
  }
}
