using System;
using FileFormat.Core;

namespace FileFormat.InterlaceCharacterEditor;

/// <summary>Turns a picture into the colour registers, font and character maps a mode needs.</summary>
internal static class IceEncoder {

  /// <summary>Picks the colour registers for a mode and lays them out as its header.</summary>
  public static byte[] ChooseColors(IceMode mode, byte[] bgra, byte[] gtia) {
    var header = new byte[IceLayout.HeaderSizeFor(mode)];
    header[0] = 1;

    switch (mode) {
      case IceMode.SuperIrg: {
        // Both frames share four registers, so four colours are all there is to choose.
        var picked = _Quantize(bgra, gtia, 4);
        header[1] = picked[0];
        header[2] = picked[1];
        header[3] = picked[2];
        header[4] = picked[3];
        break;
      }

      case IceMode.SuperIrg2: {
        // The background is shared but each frame gets its own three playfield registers.
        var picked = _Quantize(bgra, gtia, 7);
        header[1] = picked[0];
        header[2] = picked[1];
        header[4] = picked[2];
        header[6] = picked[3];
        header[3] = picked[4];
        header[5] = picked[5];
        header[7] = picked[6];
        break;
      }

      case IceMode.Min: {
        // The second frame walks the sixteen luminances of the background register's hue, so that
        // register keeps its hue and gives up its luminance.
        var picked = _Quantize(bgra, gtia, 4);
        header[1] = (byte)(picked[0] & 240);
        header[2] = picked[1];
        header[3] = picked[2];
        header[4] = picked[3];
        break;
      }

      case IceMode.Cin: {
        // The first frame's background is forced to zero here, and the header byte it would have
        // used carries the luminance the second frame's sixteen hues are drawn at.
        var picked = _Quantize(bgra, gtia, 4);
        header[1] = (byte)(_MeanLuminance(bgra) & 14);
        header[2] = picked[1];
        header[3] = picked[2];
        header[4] = picked[3];
        break;
      }

      case IceMode.Pcin: {
        // Nine registers, three of which the first frame also draws from.
        var picked = _Quantize(bgra, gtia, 9);
        header[1] = picked[0];
        header[5] = picked[1];
        header[6] = picked[2];
        header[7] = picked[3];
        header[2] = picked[4];
        header[3] = picked[5];
        header[4] = picked[6];
        header[8] = picked[7];
        header[9] = picked[8];
        break;
      }

      default:
        throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Interlace Character Editor mode.");
    }

    return header;
  }

  /// <summary>Reduces the picture to a set of Atari colour bytes.</summary>
  private static byte[] _Quantize(byte[] bgra, byte[] gtia, int count) {
    var quantized = ColorQuantizer.Quantize(bgra, IceLayout.DisplayWidth * IceLayout.DisplayHeight, count);
    var result = new byte[count];
    for (var i = 0; i < count && i < quantized.Count; ++i)
      result[i] = Atari8BitGraphics.FindNearestColorByte(
        gtia, quantized.Palette[i * 3], quantized.Palette[i * 3 + 1], quantized.Palette[i * 3 + 2]);

    return result;
  }

  /// <summary>The picture's average brightness, on the hardware's sixteen-step luminance scale.</summary>
  private static int _MeanLuminance(byte[] bgra) {
    long total = 0;
    var pixels = IceLayout.DisplayWidth * IceLayout.DisplayHeight;
    for (var i = 0; i < pixels; ++i)
      total += (bgra[i * 4 + 2] * 299 + bgra[i * 4 + 1] * 587 + bgra[i * 4] * 114) / 1000;

    return (int)(total / pixels * 15 / 255);
  }

  /// <summary>
  /// Chooses, for every pixel, which value each frame draws so that the two average as closely as
  /// possible to the picture.
  /// </summary>
  /// <remarks>
  /// The frames disagree on resolution — mode 4 changes colour every two screen pixels, the GTIA
  /// modes every four — so the choice is made over the wider of the two units: for each candidate
  /// value of the second frame, the first frame's best answer is found independently in each of
  /// its narrower slots, and the candidate with the lowest total wins.
  /// </remarks>
  public static (byte[] First, byte[] Second) ChooseValues(
    byte[] bgra, byte[] gtia, byte[] firstColors, byte[] secondColors, IceFrameKind kind) {

    var unitWidth = IceLayout.PixelsPerValue(kind);
    var secondCount = secondColors.Length;
    var slots = unitWidth / 2;

    // Every pairing of the two frames' registers, averaged as the flicker does.
    var blend = new byte[IceLayout.Graphics12ColorCount * secondCount * 3];
    for (var v1 = 0; v1 < IceLayout.Graphics12ColorCount; ++v1)
    for (var v2 = 0; v2 < secondCount; ++v2)
    for (var channel = 0; channel < 3; ++channel)
      blend[(v1 * secondCount + v2) * 3 + channel] =
        (byte)((gtia[firstColors[v1] * 3 + channel] + gtia[secondColors[v2] * 3 + channel]) >> 1);

    var first = new byte[IceLayout.DisplayWidth * IceLayout.DisplayHeight];
    var second = new byte[IceLayout.DisplayWidth * IceLayout.DisplayHeight];
    var picks = new int[slots];
    var best = new int[slots];

    for (var y = 0; y < IceLayout.DisplayHeight; ++y)
    for (var x0 = 0; x0 < IceLayout.DisplayWidth; x0 += unitWidth) {
      var bestCost = long.MaxValue;
      var bestSecond = 0;

      for (var v2 = 0; v2 < secondCount; ++v2) {
        long cost = 0;
        for (var slot = 0; slot < slots; ++slot) {
          var pixel = (y * IceLayout.DisplayWidth + x0 + slot * 2) * 4;
          var slotBest = long.MaxValue;
          var slotValue = 0;
          for (var v1 = 0; v1 < IceLayout.Graphics12ColorCount; ++v1) {
            var offset = (v1 * secondCount + v2) * 3;
            var distance = _Distance(blend, offset, bgra, pixel) + _Distance(blend, offset, bgra, pixel + 4);
            if (distance >= slotBest)
              continue;

            slotBest = distance;
            slotValue = v1;
          }

          cost += slotBest;
          picks[slot] = slotValue;
        }

        if (cost >= bestCost)
          continue;

        bestCost = cost;
        bestSecond = v2;
        picks.CopyTo(best, 0);
      }

      for (var offset = 0; offset < unitWidth; ++offset) {
        var index = y * IceLayout.DisplayWidth + x0 + offset;
        first[index] = (byte)best[offset >> 1];
        second[index] = (byte)bestSecond;
      }
    }

    return (first, second);
  }

  private static long _Distance(byte[] blend, int blendOffset, byte[] bgra, int pixel) {
    int dr = blend[blendOffset] - bgra[pixel + 2];
    int dg = blend[blendOffset + 1] - bgra[pixel + 1];
    int db = blend[blendOffset + 2] - bgra[pixel];

    return dr * dr + dg * dg + db * db;
  }

  /// <summary>
  /// Lays a frame's chosen values out as glyphs and a character map.
  /// </summary>
  /// <remarks>
  /// Each of the eight bands has its own slice of the font and holds only 120 cells, so every cell
  /// is simply given the glyph numbered after its position in the band. No two cells then share a
  /// glyph and the picture comes out exactly as chosen.
  /// </remarks>
  public static void WriteFrame(byte[] font, byte[] characters, int frame, byte[] values, IceFrameKind kind) {
    for (var y = 0; y < IceLayout.DisplayHeight; ++y) {
      var characterRow = y >> 3;
      var bank = characterRow / IceLayout.RowsPerBank;

      for (var col = 0; col < IceLayout.Columns; ++col) {
        var cell = (characterRow % IceLayout.RowsPerBank) * IceLayout.Columns + col;
        characters[characterRow * IceLayout.Columns + col] = (byte)cell;

        var source = y * IceLayout.DisplayWidth + (col << 3);
        int glyph;
        if (kind == IceFrameKind.Graphics12) {
          glyph = 0;
          for (var pair = 0; pair < 4; ++pair)
            glyph |= (values[source + pair * 2] & 3) << (6 - (pair << 1));
        } else {
          glyph = ((values[source] & 15) << 4) | (values[source + 4] & 15);
        }

        font[frame * IceLayout.FontBaseStride + (bank * 256 + cell) * IceLayout.GlyphSize + (y & 7)] = (byte)glyph;
      }
    }
  }
}
