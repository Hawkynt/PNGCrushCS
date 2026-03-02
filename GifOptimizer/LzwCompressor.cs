using System;
using System.IO;

namespace GifOptimizer;

internal static partial class LzwCompressor {
  /// <summary>
  ///   Compresses pixel data using GIF-variant LZW.
  ///   Returns raw LZW-compressed bytes (NOT sub-blocked).
  ///   Caller must write min code size byte and wrap in GIF sub-blocks.
  /// </summary>
  public static byte[] Compress(ReadOnlySpan<byte> pixels, byte bitsPerPixel, bool deferClearCodes = false) {
    using var ms = new MemoryStream();

    var clearCode = (ushort)(1 << bitsPerPixel);
    var eoiCode = (ushort)(clearCode + 1);

    uint bitBuffer = 0;
    var bitsInBuffer = 0;

    void WriteBits(ushort value, byte numBits) {
      bitBuffer |= (uint)value << bitsInBuffer;
      bitsInBuffer += numBits;
      while (bitsInBuffer >= 8) {
        ms.WriteByte((byte)(bitBuffer & 0xFF));
        bitBuffer >>= 8;
        bitsInBuffer -= 8;
      }
    }

    var table = new LzwHashTable();
    ushort nextCode;
    byte codeSize;

    void ResetTable() {
      table.Reset();
      nextCode = (ushort)(eoiCode + 1);
      codeSize = (byte)(bitsPerPixel + 1);
    }

    ResetTable();
    WriteBits(clearCode, codeSize);

    if (pixels.Length == 0) {
      WriteBits(eoiCode, codeSize);
      if (bitsInBuffer > 0)
        ms.WriteByte((byte)bitBuffer);
      return ms.ToArray();
    }

    var w = (int)pixels[0];

    // Deferred clear code tracking
    var inputSinceCheck = 0;
    var outputBitsSinceCheck = 0;
    var prevRatio = 0.0;
    var checkInterval = 64;

    for (var i = 1; i < pixels.Length; ++i) {
      var k = pixels[i];
      var wk = ((long)w << 16) | k;

      if (table.TryGet(wk, out var existing)) {
        w = existing;
        ++inputSinceCheck;
        continue;
      }

      WriteBits((ushort)w, codeSize);
      outputBitsSinceCheck += codeSize;
      ++inputSinceCheck;

      if (nextCode < 4096) {
        table.Add(wk, nextCode);
        ++nextCode;

        if (nextCode > 1 << codeSize && codeSize < 12)
          ++codeSize;
      } else if (deferClearCodes) {
        // Deferred mode: keep using the full table until ratio degrades
        if (inputSinceCheck >= checkInterval) {
          var currentRatio = inputSinceCheck > 0 ? (double)outputBitsSinceCheck / (inputSinceCheck * 8) : 1.0;
          if (prevRatio > 0 && currentRatio > prevRatio * 1.1) {
            // Ratio degraded by > 10%; emit clear and reset
            WriteBits(clearCode, codeSize);
            ResetTable();
            checkInterval = 64;
          } else {
            checkInterval = Math.Min(checkInterval * 2, 1024);
          }

          prevRatio = currentRatio;
          inputSinceCheck = 0;
          outputBitsSinceCheck = 0;
        }
      } else {
        // Standard mode: table full — emit clear and reset
        WriteBits(clearCode, codeSize);
        ResetTable();
      }

      w = k;
    }

    WriteBits((ushort)w, codeSize);
    WriteBits(eoiCode, codeSize);

    if (bitsInBuffer > 0)
      ms.WriteByte((byte)bitBuffer);

    return ms.ToArray();
  }
}
