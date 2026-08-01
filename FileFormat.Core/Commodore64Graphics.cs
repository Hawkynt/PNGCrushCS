using System;

namespace FileFormat.Core;

/// <summary>Primitives shared by the Commodore 64 picture formats.</summary>
/// <remarks>
/// The machine has no palette to store: all sixteen colours are fixed in the VIC-II, and a file
/// only ever names them by index. Every C64 format therefore needs the same table, which is why it
/// lives here instead of in each of them.
/// </remarks>
public static class Commodore64Graphics {

  /// <summary>Colours the VIC-II can show.</summary>
  public const int ColorCount = 16;

  /// <summary>The fixed sixteen colours as 0xRRGGBB values, in hardware index order.</summary>
  /// <remarks>
  /// Measured from a VIC-II rather than idealised. The widely-copied table of round numbers
  /// (0x880000 for red, 0xAAFFEE for cyan) is a reconstruction from the chip's documented voltage
  /// levels and does not match a television: the real colours are duller, and the two greys are not
  /// where an even ramp would put them. These are Pepto's measurements, which is what the reference
  /// decoder uses, so our output and it agree exactly rather than approximately.
  /// </remarks>
  public static ReadOnlySpan<int> HexColors => [
    0x000000, 0xFFFFFF, 0x68372B, 0x70A4B2, 0x6F3D86, 0x588D43,
    0x352879, 0xB8C76F, 0x6F4F25, 0x433900, 0x9A6759, 0x444444,
    0x6C6C6C, 0x9AD284, 0x6C5EB5, 0x959595
  ];

  /// <summary>The palette as RGB triplets, ready for <see cref="RawImage.Palette"/>.</summary>
  public static byte[] CreatePalette() {
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var color = HexColors[i];
      palette[i * 3] = (byte)(color >> 16);
      palette[i * 3 + 1] = (byte)(color >> 8);
      palette[i * 3 + 2] = (byte)color;
    }

    return palette;
  }

  /// <summary>Character cells across a screen.</summary>
  public const int Columns = 40;

  /// <summary>Pixel rows in one character cell.</summary>
  public const int CellHeight = 8;

  /// <summary>
  /// Decodes a standard multicolour screen: a bitmap of two bits per pixel, a video matrix holding
  /// two colours per cell and a colour RAM holding a third, with pattern 00 taken from the shared
  /// background register.
  /// </summary>
  /// <remarks>
  /// This is the layout Koala Painter established and roughly a dozen other programs copied
  /// verbatim, differing only in what they wrap it in.
  /// </remarks>
  public static RawImage DecodeMulticolor(
    ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> videoMatrix, ReadOnlySpan<byte> colorRam,
    byte background, int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellIndex = (y / CellHeight) * Columns + x / 4;
      var bitmapByte = bitmap[cellIndex * CellHeight + y % CellHeight];
      var pattern = (bitmapByte >> ((3 - x % 4) * 2)) & 3;

      var colorIndex = pattern switch {
        0 => background & 0x0F,
        1 => (videoMatrix[cellIndex] >> 4) & 0x0F,
        2 => videoMatrix[cellIndex] & 0x0F,
        _ => colorRam[cellIndex] & 0x0F,
      };

      _WriteRgb(rgb, (y * width + x) * 3, colorIndex);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// Decodes a standard high-resolution screen: one bit per pixel choosing between the two colours
  /// the cell's video matrix byte names, foreground in the high nibble.
  /// </summary>
  public static RawImage DecodeHires(ReadOnlySpan<byte> bitmap, ReadOnlySpan<byte> screenRam, int width, int height) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellIndex = (y / CellHeight) * Columns + x / 8;
      var bitmapByte = bitmap[cellIndex * CellHeight + y % CellHeight];
      var attribute = screenRam[cellIndex];
      var colorIndex = ((bitmapByte >> (7 - x % 8)) & 1) == 1 ? (attribute >> 4) & 0x0F : attribute & 0x0F;

      _WriteRgb(rgb, (y * width + x) * 3, colorIndex);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  /// <summary>
  /// Decodes a multicolour FLI screen straight out of an unparsed payload: the bitmap first, then
  /// one video matrix per line within a cell, then a single colour RAM.
  /// </summary>
  /// <remarks>
  /// FLI switches the video matrix every scanline, which is why the bank is chosen by the row
  /// inside the cell rather than being fixed. Short files are common — the trailing banks are
  /// simply absent — so everything past the bitmap is read defensively and a truncated file
  /// degrades to a two-colour silhouette rather than throwing.
  /// </remarks>
  public static RawImage DecodeFliMulticolor(
    ReadOnlySpan<byte> data, int width, int height,
    int minPayloadSize, int bitmapSize, int screenBankCount, int screenBankSize, int totalScreenSize) {
    var rgb = new byte[width * height * 3];
    var hasFullData = data.Length >= minPayloadSize;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellIndex = (y / CellHeight) * Columns + x / 4;
      var rowInCell = y % CellHeight;
      var bitmapOffset = cellIndex * CellHeight + rowInCell;
      var bitmapByte = bitmapOffset < data.Length ? data[bitmapOffset] : (byte)0;
      var pattern = (bitmapByte >> ((3 - x % 4) * 2)) & 3;

      int colorIndex;
      if (hasFullData) {
        var screenOffset = bitmapSize + rowInCell % screenBankCount * screenBankSize + cellIndex;
        var screenByte = screenOffset < data.Length ? data[screenOffset] : (byte)0;
        var colorOffset = bitmapSize + totalScreenSize + cellIndex;
        var colorByte = colorOffset < data.Length ? data[colorOffset] : (byte)0;

        colorIndex = pattern switch {
          0 => 0,
          1 => (screenByte >> 4) & 0x0F,
          2 => screenByte & 0x0F,
          _ => colorByte & 0x0F,
        };
      } else
        colorIndex = pattern != 0 ? 1 : 0;

      _WriteRgb(rgb, (y * width + x) * 3, colorIndex);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static void _WriteRgb(byte[] rgb, int offset, int colorIndex) {
    var color = HexColors[colorIndex];
    rgb[offset] = (byte)(color >> 16);
    rgb[offset + 1] = (byte)(color >> 8);
    rgb[offset + 2] = (byte)color;
  }

  /// <summary>
  /// Decodes a four-colour bitmap in the C64's cell layout into RGB triplets.
  /// </summary>
  /// <param name="shift">
  /// How far left the picture sits. Interlaced formats displace their second field by a pixel, and
  /// the column that falls off the left has nothing to show but colour zero.
  /// </param>
  /// <remarks>
  /// Two bits a pixel against four freely chosen colours, with no per-cell attributes at all — the
  /// whole screen shares one set. Several logo editors use it because a logo needs few colours and
  /// gains more from being able to place them anywhere than from having more of them.
  /// </remarks>
  /// <summary>Packs a picture into a four-colour bitmap, the inverse of decoding one.</summary>
  /// <remarks>
  /// Two bits a pixel, and the bytes run down each character cell before moving to the next — which
  /// is why the address mixes the row's low three bits in at the bottom rather than multiplying by
  /// a stride. A pixel is drawn two wide, so only every other column is stored.
  /// </remarks>
  public static byte[] PackFourColor(
    ReadOnlySpan<byte> indices, int offset, int shift, int width, int height, int size) {
    var data = new byte[size];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var source = x - shift;
      if (source < 0)
        continue;

      var at = offset + (y & ~7) * Columns + (source & ~7) + (y & 7);
      if (at < 0 || at >= data.Length)
        continue;

      data[at] |= (byte)((indices[y * width + x] & 3) << (~source & 6));
    }

    return data;
  }

  public static byte[] DecodeFourColor(
    ReadOnlySpan<byte> data, int offset, int shift, int width, int height, ReadOnlySpan<byte> palette) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var source = x - shift;
      var index = 0;
      if (source >= 0) {
        var at = offset + (y & ~7) * Columns + (source & ~7) + (y & 7);
        var b = at >= 0 && at < data.Length ? data[at] : 0;
        index = (b >> (~source & 6)) & 3;
      }

      var entry = index * 3;
      var target = (y * width + x) * 3;
      rgb[target] = palette[entry];
      rgb[target + 1] = palette[entry + 1];
      rgb[target + 2] = palette[entry + 2];
    }

    return rgb;
  }

  /// <summary>The index of the colour closest to a given one, by squared distance in RGB.</summary>
  public static int FindNearestColorIndex(byte red, byte green, byte blue) {
    var best = 0;
    var bestDistance = int.MaxValue;
    for (var i = 0; i < ColorCount; ++i) {
      var color = HexColors[i];
      int dr = ((color >> 16) & 0xFF) - red, dg = ((color >> 8) & 0xFF) - green, db = (color & 0xFF) - blue;
      var distance = dr * dr + dg * dg + db * db;
      if (distance >= bestDistance)
        continue;

      bestDistance = distance;
      best = i;
    }

    return best;
  }

  /// <summary>
  /// Encodes a high-resolution screen: one bit a pixel choosing between the two colours a cell's
  /// video matrix byte names, foreground in the high nibble.
  /// </summary>
  /// <remarks>
  /// A cell may show two of the sixteen colours and no more, so which two is the whole of the
  /// decision. Every pair is tried and the one with the least total error kept — a hundred and
  /// twenty pairs over sixty-four pixels, which is cheap and exact. Choosing the two commonest
  /// colours instead is faster and wrong: in a cell holding three near-identical shades and one
  /// contrasting mark, the mark is rare and the frequency count discards it, which is the pixel
  /// most visible to anyone looking at the picture.
  /// </remarks>
  public static void EncodeHires(
    ReadOnlySpan<byte> rgb, int width, int height, Span<byte> bitmap, Span<byte> screenRam) {
    Span<int> indices = stackalloc int[CellHeight * 8];

    for (var top = 0; top < height; top += CellHeight)
    for (var left = 0; left < width; left += 8) {
      for (var y = 0; y < CellHeight; ++y)
      for (var x = 0; x < 8; ++x) {
        var at = ((top + y) * width + left + x) * 3;
        indices[y * 8 + x] = FindNearestColorIndex(rgb[at], rgb[at + 1], rgb[at + 2]);
      }

      var (foreground, background) = _ChoosePair(indices);

      var cell = top / CellHeight * Columns + left / 8;
      for (var y = 0; y < CellHeight; ++y) {
        var row = 0;
        for (var x = 0; x < 8; ++x)
          if (_Distance(indices[y * 8 + x], foreground) <= _Distance(indices[y * 8 + x], background))
            row |= 1 << (7 - x);

        bitmap[cell * CellHeight + y] = (byte)row;
      }

      screenRam[cell] = (byte)((foreground << 4) | background);
    }
  }

  /// <summary>The two colours that between them describe a cell with the least total error.</summary>
  private static (int Foreground, int Background) _ChoosePair(ReadOnlySpan<int> indices) {
    int bestForeground = 0, bestBackground = 0;
    var bestError = long.MaxValue;

    for (var first = 0; first < ColorCount; ++first)
    for (var second = 0; second <= first; ++second) {
      long error = 0;
      foreach (var index in indices)
        error += Math.Min(_Distance(index, first), _Distance(index, second));

      if (error >= bestError)
        continue;

      bestError = error;
      bestForeground = first;
      bestBackground = second;
    }

    return (bestForeground, bestBackground);
  }

  /// <summary>Squared distance in RGB between two of the machine's colours.</summary>
  private static int _Distance(int left, int right) {
    if (left == right)
      return 0;

    int a = HexColors[left], b = HexColors[right];
    int dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
    int dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
    int db = (a & 0xFF) - (b & 0xFF);

    return dr * dr + dg * dg + db * db;
  }

  /// <summary>
  /// Encodes a standard multicolour screen: two bits a pixel, the video matrix holding two of the
  /// cell's colours and the colour RAM a third, with pattern 00 taken from the shared background.
  /// </summary>
  /// <remarks>
  /// Three of the four colours a cell shows are its own; the fourth is one register the whole screen
  /// shares. That register is chosen first, and chosen as the colour that appears most often across
  /// the picture — every cell gets it free, so spending it on a common colour leaves all three of
  /// each cell's own entries for what varies.
  /// <para/>
  /// Within a cell the search is over the colours actually present rather than all sixteen. A cell
  /// is thirty-two pixels and can hold at most that many distinct colours, usually far fewer, and a
  /// colour that appears nowhere in the cell cannot reduce its error.
  /// </remarks>
  /// <param name="fixedBackground">
  /// The background register to use, or -1 to choose one. Some formats have no register to store it
  /// in and always show black behind pattern 00, and those must be told so rather than left to pick.
  /// </param>
  /// <param name="fixedThirdColor">
  /// The colour pattern 11 must use everywhere, or -1 to let each cell choose its own. A few formats
  /// have no colour RAM at all and keep a single entry for the whole screen, and those must be told
  /// so — letting the cells choose freely and then collapsing the result afterwards would throw away
  /// the choice rather than make it.
  /// </param>
  /// <returns>The background register the whole screen shares.</returns>
  public static byte EncodeMulticolor(
    ReadOnlySpan<byte> rgb, int width, int height,
    Span<byte> bitmap, Span<byte> videoMatrix, Span<byte> colorRam,
    int fixedBackground = -1, int fixedThirdColor = -1) {
    var indices = new int[width * height];
    Span<int> totals = stackalloc int[ColorCount];

    for (var i = 0; i < indices.Length; ++i) {
      indices[i] = FindNearestColorIndex(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]);
      ++totals[indices[i]];
    }

    var background = 0;
    if (fixedBackground >= 0)
      background = fixedBackground & 0x0F;
    else
      for (var i = 1; i < ColorCount; ++i)
        if (totals[i] > totals[background])
          background = i;

    Span<int> cell = stackalloc int[CellHeight * 4];
    Span<int> present = stackalloc int[ColorCount];
    Span<int> chosen = stackalloc int[3];

    for (var top = 0; top < height; top += CellHeight)
    for (var left = 0; left < width; left += 4) {
      for (var y = 0; y < CellHeight; ++y)
      for (var x = 0; x < 4; ++x)
        cell[y * 4 + x] = indices[(top + y) * width + left + x];

      var count = 0;
      foreach (var index in cell) {
        var seen = false;
        for (var i = 0; i < count && !seen; ++i)
          seen = present[i] == index;

        if (!seen)
          present[count++] = index;
      }

      _ChooseTriple(cell, present[..count], background, chosen, fixedThirdColor);

      var at = top / CellHeight * Columns + left / 4;
      for (var y = 0; y < CellHeight; ++y) {
        var row = 0;
        for (var x = 0; x < 4; ++x)
          row |= _Pattern(cell[y * 4 + x], background, chosen) << ((3 - x) * 2);

        bitmap[at * CellHeight + y] = (byte)row;
      }

      videoMatrix[at] = (byte)((chosen[0] << 4) | chosen[1]);
      colorRam[at] = (byte)chosen[2];
    }

    return (byte)background;
  }

  /// <summary>Which of the four available colours describes a pixel with the least error.</summary>
  private static int _Pattern(int index, int background, ReadOnlySpan<int> chosen) {
    var pattern = 0;
    var best = _Distance(index, background);

    for (var i = 0; i < 3; ++i) {
      var distance = _Distance(index, chosen[i]);
      if (distance >= best)
        continue;

      best = distance;
      pattern = i + 1;
    }

    return pattern;
  }

  /// <summary>The three colours that, beside the shared background, describe a cell best.</summary>
  private static void _ChooseTriple(
    ReadOnlySpan<int> cell, ReadOnlySpan<int> present, int background, Span<int> chosen,
    int fixedThirdColor) {
    chosen[0] = chosen[1] = background;
    chosen[2] = fixedThirdColor >= 0 ? fixedThirdColor : background;

    var bestError = long.MaxValue;
    for (var a = 0; a < present.Length; ++a)
    for (var b = a; b < present.Length; ++b) {
      // The third entry is either the cell's to pick or the screen's, already decided.
      var last = fixedThirdColor >= 0 ? b : present.Length - 1;
      for (var c = b; c <= last; ++c) {
        var third = fixedThirdColor >= 0 ? fixedThirdColor : present[c];

        long error = 0;
        foreach (var index in cell)
          error += Math.Min(
            _Distance(index, background),
            Math.Min(_Distance(index, present[a]), Math.Min(_Distance(index, present[b]), _Distance(index, third))));

        if (error >= bestError)
          continue;

        bestError = error;
        chosen[0] = present[a];
        chosen[1] = present[b];
        chosen[2] = third;
      }
    }
  }
}
