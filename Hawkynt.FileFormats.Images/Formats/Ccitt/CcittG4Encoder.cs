using System;
using System.IO;

namespace FileFormat.Ccitt;

/// <summary>Encodes raw 1bpp scanlines to CCITT Group 4 (T.6) compressed data.</summary>
/// <remarks>
/// The mirror of <see cref="CcittG4Decoder"/>, and it had the mirror of its faults: it hunted for
/// "the first pixel of the opposite colour" rather than the first change to it, so a1 and b1 landed
/// in the wrong places on any line with more than one run. libtiff's verdict on the result was
/// "Line length mismatch at line 12 (got 46, expected 40)" — the lines it produced did not add up
/// to the width they claimed.
/// </remarks>
internal static class CcittG4Encoder {

  // Vertical mode codes: V(0), V(-1), V(+1), V(-2), V(+2), V(-3), V(+3)
  private static readonly (int Code, int BitLength)[] _VerticalCodes = [
    (0b1, 1),        // V(0)
    (0b010, 3),      // VL(1)
    (0b011, 3),      // VR(1)
    (0b000010, 6),   // VL(2)
    (0b000011, 6),   // VR(2)
    (0b0000010, 7),  // VL(3)
    (0b0000011, 7),  // VR(3)
  ];

  // Pass mode code
  private const int _PassCode = 0b0001;
  private const int _PassBitLength = 4;

  // Horizontal mode prefix
  private const int _HorizontalCode = 0b001;
  private const int _HorizontalBitLength = 3;

  // EOFB (End of Facsimile Block): two consecutive EOL codes
  private const int _EofbCode = 0b000000000001;
  private const int _EofbBitLength = 12;

  /// <summary>The largest run a single make-up code can name.</summary>
  private const int _MaxMakeUpRun = 2560;

  /// <summary>Encodes 1bpp pixel data — a set bit meaning black — to Group 4 compressed bytes.</summary>
  internal static byte[] Encode(byte[] pixelData, int width, int height) {
    ArgumentNullException.ThrowIfNull(pixelData);
    if (width <= 0 || height <= 0)
      return [];

    var bytesPerRow = (width + 7) / 8;
    using var ms = new MemoryStream();
    var bitPos = 0;
    var currentByte = 0;

    // The line above the first is imaginary and all white, which is a line with no changes at all.
    var refChanges = new int[width + 2];
    var curChanges = new int[width + 2];
    var refCount = 0;

    for (var row = 0; row < height; ++row) {
      var curCount = CcittChangingElements.Collect(pixelData, row * bytesPerRow, width, curChanges);
      _EncodeLine(ref currentByte, ref bitPos, ms, curChanges, curCount, refChanges, refCount, width);
      (refChanges, curChanges) = (curChanges, refChanges);
      refCount = curCount;
    }

    // Write EOFB (two EOL codes)
    _WriteBits(ref currentByte, ref bitPos, ms, _EofbCode, _EofbBitLength);
    _WriteBits(ref currentByte, ref bitPos, ms, _EofbCode, _EofbBitLength);

    if (bitPos > 0)
      ms.WriteByte((byte)(currentByte << (8 - bitPos)));

    return ms.ToArray();
  }

  private static void _EncodeLine(
    ref int currentByte, ref int bitPos, MemoryStream ms,
    int[] curChanges, int curCount, int[] refChanges, int refCount, int width) {

    // a0 starts just off the left edge, on an imaginary white pixel.
    var a0 = -1;
    var white = true;

    while (a0 < width) {
      var a1Index = CcittChangingElements.NextOfOppositeColour(curChanges, curCount, a0, white);
      var a1 = a1Index < curCount ? curChanges[a1Index] : width;

      var b1Index = CcittChangingElements.NextOfOppositeColour(refChanges, refCount, a0, white);
      var b1 = b1Index < refCount ? refChanges[b1Index] : width;
      var b2 = b1Index + 1 < refCount ? refChanges[b1Index + 1] : width;

      if (b2 < a1) {
        // Pass: this line's run reaches past both changes above it, so nothing is coded here.
        _WriteBits(ref currentByte, ref bitPos, ms, _PassCode, _PassBitLength);
        a0 = b2;
        continue;
      }

      var diff = a1 - b1;
      if (diff is >= -3 and <= 3) {
        var index = diff switch {
          0 => 0,
          -1 => 1,
          1 => 2,
          -2 => 3,
          2 => 4,
          -3 => 5,
          _ => 6
        };
        _WriteBits(ref currentByte, ref bitPos, ms, _VerticalCodes[index].Code, _VerticalCodes[index].BitLength);
        a0 = a1;
        white = !white;
        continue;
      }

      // Horizontal: a1 is too far from b1 to name as an offset, so both runs are spelled out.
      var a2 = a1Index + 1 < curCount ? curChanges[a1Index + 1] : width;
      var start = a0 < 0 ? 0 : a0;
      _WriteBits(ref currentByte, ref bitPos, ms, _HorizontalCode, _HorizontalBitLength);
      _EncodeRunLength(ref currentByte, ref bitPos, ms, a1 - start, isBlack: !white);
      _EncodeRunLength(ref currentByte, ref bitPos, ms, a2 - a1, isBlack: white);
      a0 = a2; // two runs bring the colour back to where it started
    }
  }

  /// <summary>Writes one run as at most one make-up code followed by exactly one terminating code.</summary>
  /// <remarks>
  /// A run is allowed one make-up and one terminating code, not a chain of make-ups: emitting 1728
  /// and then 256 to mean 1984 produces a stream that decoders reject. Only a run longer than 2560,
  /// which no single code can name, repeats the largest one.
  /// </remarks>
  private static void _EncodeRunLength(ref int currentByte, ref int bitPos, MemoryStream ms, int runLength, bool isBlack) {
    while (runLength > _MaxMakeUpRun) {
      _WriteMakeUp(ref currentByte, ref bitPos, ms, _MaxMakeUpRun, isBlack);
      runLength -= _MaxMakeUpRun;
    }

    if (runLength >= 64) {
      var makeUp = (runLength / 64) * 64;
      _WriteMakeUp(ref currentByte, ref bitPos, ms, makeUp, isBlack);
      runLength -= makeUp;
    }

    var term = (isBlack ? CcittHuffmanTable.BlackTerminating : CcittHuffmanTable.WhiteTerminating)[runLength];
    _WriteBits(ref currentByte, ref bitPos, ms, term.Code, term.BitLength);
  }

  private static void _WriteMakeUp(ref int currentByte, ref int bitPos, MemoryStream ms, int run, bool isBlack) {
    // From 1792 up the two colours share one table.
    var (code, bitLength) = run >= 1792
      ? CcittHuffmanTable.SharedMakeUp[(run - 1792) / 64]
      : (isBlack ? CcittHuffmanTable.BlackMakeUp : CcittHuffmanTable.WhiteMakeUp)[(run / 64) - 1];

    _WriteBits(ref currentByte, ref bitPos, ms, code, bitLength);
  }

  private static void _WriteBits(ref int currentByte, ref int bitPos, MemoryStream ms, int code, int bitLength) {
    for (var i = bitLength - 1; i >= 0; --i) {
      currentByte = (currentByte << 1) | ((code >> i) & 1);
      ++bitPos;
      if (bitPos != 8)
        continue;

      ms.WriteByte((byte)currentByte);
      currentByte = 0;
      bitPos = 0;
    }
  }
}
