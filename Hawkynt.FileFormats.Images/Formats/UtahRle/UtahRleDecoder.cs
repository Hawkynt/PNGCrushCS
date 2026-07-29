using System;

namespace FileFormat.UtahRle;

/// <summary>Decodes Utah RLE scanline opcodes to interleaved pixel data.</summary>
internal static class UtahRleDecoder {

  private const byte _OPCODE_SKIP_LINES = 1;
  private const byte _OPCODE_SET_COLOR = 2;
  private const byte _OPCODE_SKIP_PIXELS = 3;
  private const byte _OPCODE_BYTE_DATA = 5;
  private const byte _OPCODE_RUN_DATA = 6;
  private const byte _OPCODE_EOF = 7;

  public static byte[] Decode(ReadOnlySpan<byte> data, int width, int height, int numChannels, byte[]? background) {
    var pixelData = new byte[width * height * numChannels];

    if (background != null)
      _FillBackground(pixelData, width, height, numChannels, background);

    var offset = 0;
    var currentChannel = 0;
    var currentLine = 0;
    var currentPixel = 0;

    while (offset < data.Length) {
      var raw = data[offset++];
      var highBits = raw >> 6;
      int opcode;
      int count;

      if (highBits != 0) {
        // Short form: high 2 bits = opcode (1-3), low 6 bits = count
        opcode = highBits;
        count = raw & 0x3F;
      } else {
        // Long form: low 6 bits = opcode (5-7), count is 16-bit LE in next 2 bytes
        opcode = raw & 0x3F;
        if (offset + 1 >= data.Length)
          break;

        count = data[offset] | (data[offset + 1] << 8);
        offset += 2;
      }

      switch (opcode) {
        case _OPCODE_SKIP_LINES:
          currentLine += count;
          currentPixel = 0;
          break;

        case _OPCODE_SET_COLOR:
          currentChannel = count;
          currentPixel = 0;
          break;

        case _OPCODE_SKIP_PIXELS:
          currentPixel += count;
          break;

        case _OPCODE_BYTE_DATA:
          for (var i = 0; i < count && offset < data.Length; ++i) {
            var pixelIndex = currentLine * width + currentPixel;
            if (pixelIndex < width * height && currentChannel < numChannels)
              pixelData[pixelIndex * numChannels + currentChannel] = data[offset];

            ++offset;
            ++currentPixel;
          }

          break;

        case _OPCODE_RUN_DATA:
          if (offset >= data.Length)
            break;

          var runValue = data[offset++];
          for (var i = 0; i < count; ++i) {
            var pixelIndex = currentLine * width + currentPixel;
            if (pixelIndex < width * height && currentChannel < numChannels)
              pixelData[pixelIndex * numChannels + currentChannel] = runValue;

            ++currentPixel;
          }

          break;

        case _OPCODE_EOF:
          return pixelData;
      }
    }

    return pixelData;
  }

  private static void _FillBackground(byte[] pixelData, int width, int height, int numChannels, byte[] background) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        for (var c = 0; c < numChannels && c < background.Length; ++c)
          pixelData[(y * width + x) * numChannels + c] = background[c];
  }
}
