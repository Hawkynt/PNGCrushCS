using System;

namespace FileFormat.Ccitt;

/// <summary>Decodes CCITT Group 4 (T.6) compressed data to raw 1bpp scanlines.</summary>
/// <remarks>
/// Group 4 codes each row as a set of differences against the row above it, so a decoder has to
/// keep track of where the row above changes colour. The positions themselves are what the coding
/// refers to — b1 is "the first changing element on the reference line to the right of a0 and of
/// opposite colour to a0", and a changing element is a pixel that differs from the one to its left.
/// <para/>
/// Looking for the first pixel of the opposite colour instead is not the same thing, and it is what
/// this used to do: inside a run the two agree, and at every boundary after the first they do not.
/// So the row positions are carried as a list of changes rather than recovered by scanning the
/// bitmap, which is both the definition and the cheaper of the two.
/// </remarks>
internal static class CcittG4Decoder {

  /// <summary>Pass mode: the run continues past the reference line's next pair of changes.</summary>
  private const int _ModePass = 0;

  /// <summary>Horizontal mode: two run lengths follow, coded as in Group 3.</summary>
  private const int _ModeHorizontal = 1;

  /// <summary>Vertical modes sit above this, offset by their displacement index.</summary>
  private const int _ModeVerticalBase = 2;

  /// <summary>Decodes Group 4 compressed bytes to 1bpp pixel data.</summary>
  internal static byte[] Decode(byte[] compressedData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    var reader = new _BitReader(compressedData);

    // Where the row changes colour, in order. A row can change at most once per pixel, and the two
    // extra slots let a lookup past the end answer "at the right-hand edge" without a bounds test.
    var reference = new int[width + 2];
    var coding = new int[width + 2];

    // The imaginary line above the first row is all white, so it never changes.
    var referenceCount = 0;

    for (var row = 0; row < height; ++row) {
      var codingCount = _DecodeLine(reader, coding, reference, referenceCount, width);
      if (codingCount < 0)
        break;

      _WriteRow(pixelData, row * bytesPerRow, coding, codingCount, width);

      Array.Copy(coding, reference, codingCount);
      referenceCount = codingCount;
    }

    return pixelData;
  }

  /// <summary>Decodes one row, returning how many changes it has, or -1 at the end of the data.</summary>
  private static int _DecodeLine(_BitReader reader, int[] coding, int[] reference, int referenceCount, int width) {
    var a0 = -1;
    var isBlack = false;
    var count = 0;

    while (a0 < width) {
      var mode = _ReadMode(reader);
      if (mode < 0)
        return count > 0 ? count : -1;

      var (b1, b2) = _FindReferenceChanges(reference, referenceCount, a0, isBlack, width);
      var start = a0 < 0 ? 0 : a0;

      switch (mode) {
        case _ModePass:
          // The colour does not change, and no boundary is recorded — the run simply reaches b2.
          a0 = b2;
          continue;

        case _ModeHorizontal: {
          // Two runs follow: the first in the colour a0 already is, the second in the other one.
          var first = _DecodeRunLength(reader, isBlack);
          var second = _DecodeRunLength(reader, !isBlack);
          if (first < 0 || second < 0)
            return count > 0 ? count : -1;

          var a1 = Math.Min(start + first, width);
          var a2 = Math.Min(a1 + second, width);

          coding[count++] = a1;
          coding[count++] = a2;
          a0 = a2;
          continue;
        }

        default: {
          var a1 = Math.Clamp(b1 + _VerticalOffset(mode), 0, width);

          coding[count++] = a1;
          a0 = a1;
          isBlack = !isBlack;
          continue;
        }
      }
    }

    return count;
  }

  /// <summary>How far a vertical mode puts the boundary from b1.</summary>
  private static int _VerticalOffset(int mode) => (mode - _ModeVerticalBase) switch {
    0 => 0,
    1 => -1,
    2 => 1,
    3 => -2,
    4 => 2,
    5 => -3,
    6 => 3,
    _ => 0,
  };

  /// <summary>
  /// Finds b1 and b2: the next change on the reference line past a0 that turns the colour a0 is not,
  /// and the change after it.
  /// </summary>
  /// <remarks>
  /// The changes alternate, starting with white turning black, so a change's colour is decided by
  /// whether its index is even. That is why the search has to step past one more entry when the
  /// parity is wrong rather than simply taking the first change beyond a0.
  /// </remarks>
  private static (int B1, int B2) _FindReferenceChanges(
    int[] reference, int referenceCount, int a0, bool isBlack, int width) {
    var i = 0;
    while (i < referenceCount && reference[i] <= a0)
      ++i;

    // An even index turns the line black; we want the one that turns it the colour a0 is not.
    if ((i & 1) != (isBlack ? 1 : 0))
      ++i;

    return (
      i < referenceCount ? reference[i] : width,
      i + 1 < referenceCount ? reference[i + 1] : width);
  }

  /// <summary>Paints a row from its list of colour changes, the first run being white.</summary>
  private static void _WriteRow(byte[] pixelData, int offset, int[] changes, int count, int width) {
    var isBlack = false;
    var at = 0;

    for (var i = 0; i <= count; ++i) {
      var until = i < count ? Math.Min(changes[i], width) : width;
      if (isBlack)
        for (var x = at; x < until; ++x)
          pixelData[offset + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));

      if (until > at)
        at = until;

      isBlack = !isBlack;
      if (at >= width)
        return;
    }
  }

  /// <summary>Reads the next mode from the bitstream. Returns: 0=pass, 1=horizontal, 2-8=vertical modes.</summary>
  private static int _ReadMode(_BitReader reader) {
    // V(0) = 1
    var bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1)
      return 2; // V(0)

    // 0...
    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1) {
      // 01...
      bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      return bit == 0 ? 3 : 4; // VL(1) = 010, VR(1) = 011
    }

    // 00...
    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1)
      return 1; // Horizontal = 001

    // 000...
    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1)
      return 0; // Pass = 0001

    // 0000...
    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1) {
      // 00001...
      bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      return bit == 0 ? 5 : 6; // VL(2) = 000010, VR(2) = 000011
    }

    // 00000...
    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1) {
      // 000001...
      bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      return bit == 0 ? 7 : 8; // VL(3) = 0000010, VR(3) = 0000011
    }

    // EOFB or invalid
    return -1;
  }

  private static int _DecodeRunLength(_BitReader reader, bool isBlack) {
    var totalRun = 0;

    while (true) {
      var code = _DecodeNextCode(reader, isBlack);
      if (code < 0)
        return totalRun > 0 ? totalRun : -1;

      totalRun += code;

      // A make-up code says only how many multiples of 64 the run has; a terminating code below 64
      // ends it. So the loop runs until one of the short codes arrives.
      if (code < 64)
        return totalRun;
    }
  }

  private static int _DecodeNextCode(_BitReader reader, bool isBlack) {
    var termTable = isBlack ? CcittHuffmanTable.BlackTerminating : CcittHuffmanTable.WhiteTerminating;
    var makeUpTable = isBlack ? CcittHuffmanTable.BlackMakeUp : CcittHuffmanTable.WhiteMakeUp;

    var accumulated = 0;
    var bitsRead = 0;
    var maxBits = 14;

    while (bitsRead < maxBits) {
      var bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      accumulated = (accumulated << 1) | bit;
      ++bitsRead;

      for (var i = 0; i < termTable.Length; ++i)
        if (termTable[i].BitLength == bitsRead && termTable[i].Code == accumulated)
          return i;

      for (var i = 0; i < makeUpTable.Length; ++i)
        if (makeUpTable[i].BitLength == bitsRead && makeUpTable[i].Code == accumulated)
          return (i + 1) * 64;
    }

    return -1;
  }

  private sealed class _BitReader(byte[] data) {
    private int _bytePos;
    private int _bitPos = 7;

    public int ReadBit() {
      if (_bytePos >= data.Length)
        return -1;

      var bit = (data[_bytePos] >> _bitPos) & 1;
      --_bitPos;
      if (_bitPos < 0) {
        _bitPos = 7;
        ++_bytePos;
      }

      return bit;
    }
  }
}
