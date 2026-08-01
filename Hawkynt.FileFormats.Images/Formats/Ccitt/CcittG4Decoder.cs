using System;

namespace FileFormat.Ccitt;

/// <summary>Decodes CCITT Group 4 (T.6) compressed data to raw 1bpp scanlines.</summary>
/// <remarks>
/// A line is held as its changing elements — the positions where it switches colour — rather than as
/// pixels, because that is what T.6 codes against. Every line is described relative to the one above
/// it, and the three modes all answer the same question: where does the next colour change go,
/// given where the reference line changes.
///
/// The previous implementation worked on pixels and looked for "the first pixel of the opposite
/// colour" instead of the first *change* to the opposite colour, which is a different position
/// entirely on any line that is not a single run. It also read white runs out of the black Huffman
/// table in horizontal mode, and filled without clamping, so a run that reached the right-hand edge
/// walked off the end of the scanline.
/// </remarks>
internal static class CcittG4Decoder {

  private const int _MODE_PASS = 0;
  private const int _MODE_HORIZONTAL = 1;
  private const int _MODE_VERTICAL_0 = 2;

  /// <summary>Vertical mode places a1 this far from b1, indexed by mode - <see cref="_MODE_VERTICAL_0"/>.</summary>
  private static readonly int[] _VERTICAL_OFFSETS = [0, -1, 1, -2, 2, -3, 3];

  /// <summary>Decodes Group 4 compressed bytes to 1bpp pixel data, where a set bit is black.</summary>
  internal static byte[] Decode(byte[] compressedData, int width, int height) {
    ArgumentNullException.ThrowIfNull(compressedData);
    if (width <= 0 || height <= 0)
      return [];

    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    var reader = new _BitReader(compressedData);

    // The line above the first is imaginary and all white, which is a line with no changes at all.
    var refChanges = new int[width + 2];
    var curChanges = new int[width + 2];
    var refCount = 0;

    for (var row = 0; row < height; ++row) {
      var curCount = _DecodeLine(reader, refChanges, refCount, curChanges, width);
      if (curCount < 0)
        break; // EOFB, or the data ran out: whatever is left stays white

      CcittChangingElements.Render(curChanges, curCount, pixelData, row * bytesPerRow, width);
      (refChanges, curChanges) = (curChanges, refChanges);
      refCount = curCount;
    }

    return pixelData;
  }

  /// <summary>Decodes one line into <paramref name="curChanges"/>, returning how many it wrote, or -1 to stop.</summary>
  private static int _DecodeLine(_BitReader reader, int[] refChanges, int refCount, int[] curChanges, int width) {
    // a0 starts just off the left edge, on an imaginary white pixel, so the first change may be at 0.
    var a0 = -1;
    var white = true;
    var count = 0;

    while (a0 < width) {
      var mode = _ReadMode(reader);
      if (mode < 0)
        return count > 0 ? count : -1;

      var b1Index = CcittChangingElements.NextOfOppositeColour(refChanges, refCount, a0, white);
      var b1 = b1Index < refCount ? refChanges[b1Index] : width;
      var b2 = b1Index + 1 < refCount ? refChanges[b1Index + 1] : width;

      switch (mode) {
        case _MODE_PASS:
          // The run on this line carries on past b2, so nothing changes colour here.
          a0 = b2;
          break;

        case _MODE_HORIZONTAL: {
          // Two runs are spelled out: the first in a0's own colour, the second in the other.
          var start = a0 < 0 ? 0 : a0;
          var run1 = _DecodeRunLength(reader, isBlack: !white);
          var run2 = _DecodeRunLength(reader, isBlack: white);
          if (run1 < 0 || run2 < 0)
            return count > 0 ? count : -1;

          var a1 = Math.Min(start + run1, width);
          var a2 = Math.Min(a1 + run2, width);
          curChanges[count++] = a1;
          curChanges[count++] = a2;
          a0 = a2; // two runs bring the colour back to where it started
          break;
        }

        default: {
          var a1 = Math.Clamp(b1 + _VERTICAL_OFFSETS[mode - _MODE_VERTICAL_0], 0, width);
          curChanges[count++] = a1;
          a0 = a1;
          white = !white;
          break;
        }
      }

      // A well-formed line cannot change colour more often than it has pixels; a malformed one
      // must not be allowed to run off the end of the array.
      if (count > width)
        break;
    }

    return count;
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

  /// <summary>Reads a full run: any number of make-up codes followed by one terminating code.</summary>
  private static int _DecodeRunLength(_BitReader reader, bool isBlack) {
    var totalRun = 0;

    while (true) {
      var code = _DecodeNextCode(reader, isBlack);
      if (code < 0)
        return -1;

      totalRun += code;
      if (code < 64)
        return totalRun; // terminating codes are 0..63 and end the run
    }
  }

  private static int _DecodeNextCode(_BitReader reader, bool isBlack) {
    var termTable = isBlack ? CcittHuffmanTable.BlackTerminating : CcittHuffmanTable.WhiteTerminating;
    var makeUpTable = isBlack ? CcittHuffmanTable.BlackMakeUp : CcittHuffmanTable.WhiteMakeUp;
    var sharedTable = CcittHuffmanTable.SharedMakeUp;

    var accumulated = 0;
    var bitsRead = 0;
    const int maxBits = 13; // the longest code in any of the tables is a 13-bit black make-up

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

      // Runs of 1792 and above share one table between the two colours.
      for (var i = 0; i < sharedTable.Length; ++i)
        if (sharedTable[i].BitLength == bitsRead && sharedTable[i].Code == accumulated)
          return 1792 + (i * 64);
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
