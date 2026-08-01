using System.IO;

namespace FileFormat.UtahRle;

/// <summary>Encodes interleaved pixel data into Utah RLE scanline opcodes.</summary>
/// <remarks>
/// Every instruction is two bytes: a command and one operand. Setting bit six of the command says
/// the operand does not fit, and a sixteen-bit count follows instead — the operand byte is then
/// padding rather than data.
/// <para/>
/// What was written here before packed the command into the top two bits and the count into the
/// remaining six, which is a scheme of this project's own and one no reader of these has. Rows also
/// go up the picture rather than down: the origin of one of these is its bottom left corner.
/// </remarks>
internal static class UtahRleEncoder {

  private const byte _OpSkipLines = 1;
  private const byte _OpSetColor = 2;
  private const byte _OpByteData = 5;
  private const byte _OpRunData = 6;
  private const byte _OpEof = 7;

  /// <summary>Set on a command whose count needs more than the one operand byte.</summary>
  private const byte _Long = 0x40;

  /// <summary>The most a single instruction can cover, counts being stored one less than they are.</summary>
  private const int _MaxCount = 65536;

  public static byte[] Encode(byte[] pixelData, int width, int height, int numChannels) {
    using var ms = new MemoryStream();

    // Bottom row first, which is where one of these starts.
    for (var row = 0; row < height; ++row) {
      var y = height - 1 - row;
      if (row > 0)
        _Instruction(ms, _OpSkipLines, 1);

      for (var c = 0; c < numChannels; ++c) {
        _Instruction(ms, _OpSetColor, c);

        var x = 0;
        while (x < width) {
          var value = pixelData[(y * width + x) * numChannels + c];

          var run = 1;
          while (x + run < width && run < _MaxCount
                 && pixelData[(y * width + x + run) * numChannels + c] == value)
            ++run;

          if (run >= 3) {
            // A run states its length and then the byte, the byte occupying a whole pair of its own.
            _Instruction(ms, _OpRunData, run - 1);
            ms.WriteByte(value);
            ms.WriteByte(0);
            x += run;
            continue;
          }

          var start = x;
          var literals = 0;
          while (x + literals < width && literals < _MaxCount) {
            var at = x + literals;
            if (at + 2 < width) {
              var a = pixelData[(y * width + at) * numChannels + c];
              var b = pixelData[(y * width + at + 1) * numChannels + c];
              var d = pixelData[(y * width + at + 2) * numChannels + c];
              if (a == b && b == d)
                break;
            }

            ++literals;
          }

          if (literals == 0)
            literals = 1;

          _Instruction(ms, _OpByteData, literals - 1);
          for (var i = 0; i < literals; ++i)
            ms.WriteByte(pixelData[(y * width + start + i) * numChannels + c]);

          // The data is padded to an even length, instructions being two bytes throughout.
          if ((literals & 1) != 0)
            ms.WriteByte(0);

          x += literals;
        }
      }
    }

    _Instruction(ms, _OpEof, 0);
    return ms.ToArray();
  }

  /// <summary>Writes one instruction, in the short form where the operand fits and the long where it does not.</summary>
  private static void _Instruction(MemoryStream ms, byte opcode, int operand) {
    if (operand < 256) {
      ms.WriteByte(opcode);
      ms.WriteByte((byte)operand);
      return;
    }

    ms.WriteByte((byte)(opcode | _Long));
    ms.WriteByte(0);
    ms.WriteByte((byte)operand);
    ms.WriteByte((byte)(operand >> 8));
  }
}
