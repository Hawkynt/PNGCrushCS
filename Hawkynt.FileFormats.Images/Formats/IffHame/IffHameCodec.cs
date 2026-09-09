using System;

namespace FileFormat.IffHame;

/// <summary>HAM-E byte-stream and carrier-palette primitives.</summary>
internal static class IffHameCodec {

  internal const int PaletteEntriesPerLine = 64;
  internal const int SelectableEntriesPerBank = 60;
  internal const int MaximumPaletteEntries = 256;
  internal const byte RegisterMode = 0x14;
  internal const byte HoldAndModifyMode = 0x18;

  internal static ReadOnlySpan<byte> MagicCookie => [0xA2, 0xF5, 0x84, 0xDC, 0x6D, 0xB0, 0x7F];

  internal static byte[] Encode(
    ReadOnlySpan<byte> rgb,
    int width,
    int height,
    ReadOnlySpan<byte> palette,
    int paletteCount
  ) {
    if (rgb.Length != width * height * 3)
      throw new ArgumentException("HAM-E RGB input has the wrong size.", nameof(rgb));
    if (paletteCount is < 1 or > SelectableEntriesPerBank || palette.Length < paletteCount * 3)
      throw new ArgumentException($"HAM-E encoding needs between 1 and {SelectableEntriesPerBank} palette entries.", nameof(palette));

    var result = new byte[width * height];
    for (var y = 0; y < height; ++y) {
      var previousR = 0;
      var previousG = 0;
      var previousB = 0;

      for (var x = 0; x < width; ++x) {
        var sourceAt = (y * width + x) * 3;
        var targetR = rgb[sourceAt];
        var targetG = rgb[sourceAt + 1];
        var targetB = rgb[sourceAt + 2];

        var bestCode = (byte)0;
        var bestR = 0;
        var bestG = 0;
        var bestB = 0;
        var bestError = int.MaxValue;

        for (var i = 0; i < paletteCount; ++i) {
          var paletteAt = i * 3;
          var r = palette[paletteAt];
          var g = palette[paletteAt + 1];
          var b = palette[paletteAt + 2];
          var error = _SquaredError(targetR, targetG, targetB, r, g, b);
          if (error >= bestError)
            continue;

          bestError = error;
          bestCode = (byte)i;
          bestR = r;
          bestG = g;
          bestB = b;
        }

        var blue = _QuantizeComponent(targetB);
        var errorBlue = _SquaredError(targetR, targetG, targetB, previousR, previousG, blue);
        if (errorBlue < bestError) {
          bestError = errorBlue;
          bestCode = (byte)(0x40 | (blue >> 2));
          bestR = previousR;
          bestG = previousG;
          bestB = blue;
        }

        var red = _QuantizeComponent(targetR);
        var errorRed = _SquaredError(targetR, targetG, targetB, red, previousG, previousB);
        if (errorRed < bestError) {
          bestError = errorRed;
          bestCode = (byte)(0x80 | (red >> 2));
          bestR = red;
          bestG = previousG;
          bestB = previousB;
        }

        var green = _QuantizeComponent(targetG);
        var errorGreen = _SquaredError(targetR, targetG, targetB, previousR, green, previousB);
        if (errorGreen < bestError) {
          bestCode = (byte)(0xC0 | (green >> 2));
          bestR = previousR;
          bestG = green;
          bestB = previousB;
        }

        result[y * width + x] = bestCode;
        previousR = bestR;
        previousG = bestG;
        previousB = bestB;
      }
    }

    return result;
  }

  internal static byte[] Decode(
    ReadOnlySpan<byte> commands,
    int width,
    int height,
    ReadOnlySpan<byte> palette,
    int paletteCount
  ) {
    if (commands.Length != width * height)
      throw new ArgumentException("HAM-E command data has the wrong size.", nameof(commands));
    if (paletteCount is < 1 or > MaximumPaletteEntries || palette.Length < paletteCount * 3)
      throw new ArgumentException("HAM-E palette data has the wrong size.", nameof(palette));

    var result = new byte[width * height * 3];
    var bank = 0;

    for (var y = 0; y < height; ++y) {
      var r = 0;
      var g = 0;
      var b = 0;

      for (var x = 0; x < width; ++x) {
        var command = commands[y * width + x];
        var operation = command >> 6;
        var data = command & 0x3F;

        switch (operation) {
          case 0 when data < SelectableEntriesPerBank: {
            var index = bank + data;
            if (index < paletteCount) {
              var at = index * 3;
              r = palette[at];
              g = palette[at + 1];
              b = palette[at + 2];
            }
            break;
          }
          case 0:
            bank = (data - SelectableEntriesPerBank) << 6;
            break;
          case 1:
            b = data << 2;
            break;
          case 2:
            r = data << 2;
            break;
          case 3:
            g = data << 2;
            break;
        }

        var destinationAt = (y * width + x) * 3;
        result[destinationAt] = (byte)r;
        result[destinationAt + 1] = (byte)g;
        result[destinationAt + 2] = (byte)b;
      }
    }

    return result;
  }

  internal static byte[] CreateCarrierPalette() {
    var palette = new byte[16 * 3];
    for (var nibble = 0; nibble < 16; ++nibble) {
      var at = nibble * 3;
      palette[at] = (byte)((nibble & 0x08) != 0 ? 0x88 : 0x00);
      palette[at + 1] = (byte)((nibble & 0x04) != 0 ? 0x88 : 0x00);
      palette[at + 2] = (byte)(((nibble & 0x02) != 0 ? 0x88 : 0x00) | ((nibble & 0x01) != 0 ? 0x11 : 0x00));
    }
    return palette;
  }

  internal static byte CarrierIndexToNibble(byte index, ReadOnlySpan<byte> carrierPalette) {
    var at = index * 3;
    if (at + 2 >= carrierPalette.Length)
      throw new ArgumentException("HAM-E carrier references a colour outside CMAP.", nameof(carrierPalette));

    return (byte)(
      ((carrierPalette[at] & 0x80) != 0 ? 0x08 : 0)
      | ((carrierPalette[at + 1] & 0x80) != 0 ? 0x04 : 0)
      | ((carrierPalette[at + 2] & 0x80) != 0 ? 0x02 : 0)
      | ((carrierPalette[at + 2] & 0x10) != 0 ? 0x01 : 0)
    );
  }

  internal static byte[] CommandsToCarrier(ReadOnlySpan<byte> commands) {
    var carrier = new byte[commands.Length * 2];
    for (var i = 0; i < commands.Length; ++i) {
      carrier[i * 2] = (byte)(commands[i] >> 4);
      carrier[i * 2 + 1] = (byte)(commands[i] & 0x0F);
    }
    return carrier;
  }

  private static int _QuantizeComponent(int value) => Math.Min(63, (value + 2) >> 2) << 2;

  private static int _SquaredError(int targetR, int targetG, int targetB, int actualR, int actualG, int actualB) {
    var dr = targetR - actualR;
    var dg = targetG - actualG;
    var db = targetB - actualB;
    return dr * dr + dg * dg + db * db;
  }
}
