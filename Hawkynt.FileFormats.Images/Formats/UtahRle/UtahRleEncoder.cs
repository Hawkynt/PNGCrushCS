using System;
using System.IO;

namespace FileFormat.UtahRle;

/// <summary>Encodes interleaved pixel data to Utah RLE scanline opcodes.</summary>
internal static class UtahRleEncoder {

  private const byte _OPCODE_SKIP_LINES = 1;
  private const byte _OPCODE_SET_COLOR = 2;
  private const byte _OPCODE_BYTE_DATA = 5;
  private const byte _OPCODE_RUN_DATA = 6;
  private const byte _OPCODE_EOF = 7;

  public static byte[] Encode(byte[] pixelData, int width, int height, int numChannels) {
    using var ms = new MemoryStream();

    for (var y = 0; y < height; ++y) {
      if (y > 0)
        _WriteShortOpcode(ms, _OPCODE_SKIP_LINES, 1);

      for (var c = 0; c < numChannels; ++c) {
        _WriteShortOpcode(ms, _OPCODE_SET_COLOR, c);

        var x = 0;
        while (x < width) {
          var pixelIndex = (y * width + x) * numChannels + c;
          var currentValue = pixelData[pixelIndex];

          var runLength = 1;
          while (x + runLength < width && runLength < 65535) {
            var nextIndex = (y * width + x + runLength) * numChannels + c;
            if (pixelData[nextIndex] != currentValue)
              break;

            ++runLength;
          }

          if (runLength >= 3) {
            _WriteLongOpcode(ms, _OPCODE_RUN_DATA, runLength);
            ms.WriteByte(currentValue);
            x += runLength;
          } else {
            var literalStart = x;
            var literalCount = 0;
            while (x + literalCount < width && literalCount < 65535) {
              var ahead = x + literalCount;
              if (ahead + 2 < width) {
                var idx0 = (y * width + ahead) * numChannels + c;
                var idx1 = (y * width + ahead + 1) * numChannels + c;
                var idx2 = (y * width + ahead + 2) * numChannels + c;
                if (pixelData[idx0] == pixelData[idx1] && pixelData[idx1] == pixelData[idx2])
                  break;
              }

              ++literalCount;
            }

            if (literalCount == 0)
              literalCount = 1;

            _WriteLongOpcode(ms, _OPCODE_BYTE_DATA, literalCount);
            for (var i = 0; i < literalCount; ++i) {
              var idx = (y * width + literalStart + i) * numChannels + c;
              ms.WriteByte(pixelData[idx]);
            }

            x += literalCount;
          }
        }
      }
    }

    _WriteLongOpcode(ms, _OPCODE_EOF, 0);
    return ms.ToArray();
  }

  /// <summary>Writes a short-form opcode (opcodes 1-3) with count embedded in low 6 bits.</summary>
  private static void _WriteShortOpcode(MemoryStream ms, byte opcode, int count) =>
    ms.WriteByte((byte)((opcode << 6) | (count & 0x3F)));

  /// <summary>Writes a long-form opcode (opcodes 5-7) with count as a 16-bit little-endian value.</summary>
  private static void _WriteLongOpcode(MemoryStream ms, byte opcode, int count) {
    ms.WriteByte(opcode);
    ms.WriteByte((byte)(count & 0xFF));
    ms.WriteByte((byte)((count >> 8) & 0xFF));
  }
}
