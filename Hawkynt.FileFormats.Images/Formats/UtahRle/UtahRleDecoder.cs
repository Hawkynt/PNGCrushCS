using System;

namespace FileFormat.UtahRle;

/// <summary>Decodes Utah RLE scanline opcodes to interleaved pixel data.</summary>
/// <remarks>
/// Every instruction is two bytes: a command and one operand. Setting bit six of the command says
/// the operand did not fit, and a sixteen-bit count follows instead. Counts are stored one less
/// than they are, and rows run up the picture rather than down — the origin of one of these is its
/// bottom left corner.
/// </remarks>
internal static class UtahRleDecoder {

  private const byte _OpSkipLines = 1;
  private const byte _OpSetColor = 2;
  private const byte _OpSkipPixels = 3;
  private const byte _OpByteData = 5;
  private const byte _OpRunData = 6;
  private const byte _OpEof = 7;

  /// <summary>Set on a command whose count needed more than the one operand byte.</summary>
  private const byte _Long = 0x40;

  /// <param name="background">
  /// What the picture stands on where nothing is drawn. A file may state one or say it has none,
  /// in which case the untouched parts stay at zero.
  /// </param>
  public static byte[] Decode(
    ReadOnlySpan<byte> data, int width, int height, int numChannels, byte[]? background = null) {
    var pixelData = new byte[width * height * numChannels];

    if (background is { Length: > 0 })
      for (var i = 0; i < pixelData.Length; ++i)
        pixelData[i] = background[i % numChannels % background.Length];

    var at = 0;
    var channel = 0;
    var row = 0;
    var column = 0;

    while (at + 1 < data.Length) {
      var command = data[at];
      var opcode = command & 0x3F;
      int operand;

      if ((command & _Long) != 0) {
        if (at + 3 >= data.Length)
          break;

        operand = data[at + 2] | (data[at + 3] << 8);
        at += 4;
      } else {
        operand = data[at + 1];
        at += 2;
      }

      switch (opcode) {
        case _OpSkipLines:
          row += operand;
          column = 0;
          continue;

        case _OpSetColor:
          channel = operand;
          column = 0;
          continue;

        case _OpSkipPixels:
          column += operand;
          continue;

        case _OpByteData: {
          // The count is one less than the number of bytes, and the run is padded to an even length.
          var count = operand + 1;
          for (var i = 0; i < count && at < data.Length; ++i, ++at, ++column)
            _Put(pixelData, width, height, numChannels, row, column, channel, data[at]);

          if ((count & 1) != 0)
            ++at;

          continue;
        }

        case _OpRunData: {
          if (at >= data.Length)
            break;

          var value = data[at];
          at += 2;

          var count = operand + 1;
          for (var i = 0; i < count; ++i, ++column)
            _Put(pixelData, width, height, numChannels, row, column, channel, value);

          continue;
        }

        case _OpEof:
          return pixelData;
      }

      break;
    }

    return pixelData;
  }

  /// <summary>Places one sample, counting rows from the bottom of the picture.</summary>
  private static void _Put(
    byte[] pixels, int width, int height, int channels, int row, int column, int channel, byte value) {
    if (column < 0 || column >= width || row < 0 || row >= height || channel < 0 || channel >= channels)
      return;

    var y = height - 1 - row;
    pixels[(y * width + column) * channels + channel] = value;
  }
}
