using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AmstradMode5;

/// <summary>In-memory representation of a Mode 5 picture (.cm5, with a .gfx beside it).</summary>
/// <remarks>
/// The Amstrad has no mode 5. This is mode 1 — four colours, 288 pixels across — with the palette
/// rewritten every scanline, which is what the name refers to: the four colours become four per
/// row instead of four per screen, and one of them is rewritten six times across the row as well.
/// <para/>
/// So the two files divide by what changes and how often. The .gfx holds the bitmap, which does not
/// change; the .cm5 holds eight colour bytes per scanline, of which six belong to one of the four
/// pen values and let it vary across the width.
/// </remarks>
public readonly record struct AmstradMode5File
  : IImageFormatReader<AmstradMode5File>, IImageToRawImage<AmstradMode5File>,
    IImageFromRawImage<AmstradMode5File>, IImageFormatWriter<AmstradMode5File> {

  static byte[] IImageFormatWriter<AmstradMode5File>.ToBytes(AmstradMode5File file)
    => AmstradMode5Writer.ToBytes(file);

  /// <summary>Pixels across.</summary>
  public const int Width = 288;

  /// <summary>Rows.</summary>
  public const int Height = 256;

  /// <summary>Bytes one row of the bitmap occupies.</summary>
  public const int Stride = 72;

  /// <summary>Size of the file holding the colours.</summary>
  public const int FileSize = 2049;

  /// <summary>Size of the companion holding the bitmap.</summary>
  public const int BitmapFileSize = Stride * Height;

  /// <summary>Colour bytes each scanline carries.</summary>
  public const int ColorsPerRow = 8;

  /// <summary>Pixels one of the row's six varying colours covers.</summary>
  public const int ZoneWidth = 48;

  static string IImageFormatMetadata<AmstradMode5File>.PrimaryExtension => ".cm5";
  static string[] IImageFormatMetadata<AmstradMode5File>.FileExtensions => [".cm5"];
  static AmstradMode5File IImageFormatReader<AmstradMode5File>.FromSpan(ReadOnlySpan<byte> data)
    => AmstradMode5Reader.FromSpan(data);

  /// <summary>Reads the file together with the companion it cannot be shown without.</summary>
  static AmstradMode5File IImageFormatReader<AmstradMode5File>.FromFile(FileInfo file)
    => AmstradMode5Reader.FromFile(file);
  static VideoMode[] IImageFormatMetadata<AmstradMode5File>.VideoModes => [
    new("Mode 5", [(Width, Height)], [AmstradGraphics.ColorCount])
  ];

  /// <summary>The colours, eight per scanline.</summary>
  public byte[] Colors { get; init; }

  /// <summary>The bitmap from the companion file.</summary>
  public byte[] Bitmap { get; init; }

  public static RawImage ToRawImage(AmstradMode5File file) {
    var colors = file.Colors ?? [];
    var bitmap = file.Bitmap ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = y * Stride + (x >> 2);
      var b = at < bitmap.Length ? bitmap[at] : 0;

      // Mode 1 interleaves its two bits across the byte the same way mode 0 does its four.
      var pen = (b >> (~x & 3)) & 17;

      var slot = pen switch {
        0 => 3 + (y * ColorsPerRow) + x / ZoneWidth,
        1 => 1 + (y * ColorsPerRow),
        16 => 2 + (y * ColorsPerRow),
        _ => 0,
      };

      var c = slot < colors.Length ? colors[slot] : 0;
      pixels[y * Width + x] = (byte)(c - AmstradGraphics.ColorBias);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = AmstradGraphics.Palette.ToArray(),
      PaletteCount = AmstradGraphics.ColorCount,
    };
  }

  /// <summary>The extension the bitmap lives under, beside the colours.</summary>
  public const string CompanionExtension = ".gfx";

  /// <summary>Builds a picture, whose colours and bitmap are two files rather than one.</summary>
  /// <remarks>
  /// Mode 5 is not a mode the hardware has. It is two colours a pixel with the palette rewritten as
  /// the beam travels: one colour for the whole picture, two more for each scanline, and a sixth of
  /// the width getting one of its own — so a row can show eight colours and the picture as many as
  /// it has rows. The choosing follows that shape rather than reducing the picture as a whole.
  /// </remarks>
  public static AmstradMode5File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var palette = AmstradGraphics.Palette;

    var colors = new byte[FileSize];
    var bitmap = new byte[BitmapFileSize];

    // The one colour the whole picture shares, which every row falls back on.
    colors[0] = (byte)(_Nearest(rgb, palette, 0, 0, Width, Height) + AmstradGraphics.ColorBias);

    for (var y = 0; y < Height; ++y) {
      var row = 1 + y * ColorsPerRow;

      // Two colours for the row and one for each sixth of it, chosen from what those pixels are.
      colors[row] = (byte)(_Nearest(rgb, palette, 0, y, Width, 1) + AmstradGraphics.ColorBias);
      colors[row + 1] = (byte)(_SecondNearest(rgb, palette, y) + AmstradGraphics.ColorBias);

      for (var zone = 0; zone < Width / ZoneWidth; ++zone)
        colors[row + 2 + zone] =
          (byte)(_Nearest(rgb, palette, zone * ZoneWidth, y, ZoneWidth, 1) + AmstradGraphics.ColorBias);

      for (var x = 0; x < Width; ++x) {
        var pen = _ChoosePen(rgb, palette, colors, y, x);

        // Two bits a pixel, four to a byte, and the two are four bits apart rather than adjacent.
        var shift = ~x & 3;
        bitmap[y * Stride + (x >> 2)] |= (byte)(((pen & 1) << shift) | (((pen >> 4) & 1) << (shift + 4)));
      }
    }

    return new() { Colors = colors, Bitmap = bitmap };
  }

  /// <summary>Writes the bitmap file the colours are meaningless without.</summary>
  static void IImageFormatWriter<AmstradMode5File>.WriteCompanions(AmstradMode5File file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);

    var bitmap = file.Bitmap ?? new byte[BitmapFileSize];
    var padded = new byte[BitmapFileSize];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, BitmapFileSize)).CopyTo(padded);

    File.WriteAllBytes(Path.ChangeExtension(target.FullName, CompanionExtension), padded);
  }

  /// <summary>Which of the four pens a pixel should use, given what each of them shows here.</summary>
  private static int _ChoosePen(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> palette, ReadOnlySpan<byte> colors, int y, int x) {
    var row = 1 + y * ColorsPerRow;
    Span<int> pens = [17, 1, 16, 0];
    Span<int> slots = [0, row, row + 1, row + 2 + x / ZoneWidth];

    var best = 0;
    var bestCost = long.MaxValue;

    for (var i = 0; i < pens.Length; ++i) {
      var entry = (colors[slots[i]] - AmstradGraphics.ColorBias) * 3;
      if (entry < 0 || entry + 2 >= palette.Length)
        continue;

      var at = (y * Width + x) * 3;
      long dr = rgb[at] - palette[entry], dg = rgb[at + 1] - palette[entry + 1], db = rgb[at + 2] - palette[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = pens[i];
    }

    return best;
  }

  /// <summary>The machine colour suiting a rectangle of the picture best.</summary>
  private static int _Nearest(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> palette, int x0, int y0, int width, int height) {
    var best = 0;
    var bestCost = long.MaxValue;

    for (var candidate = 0; candidate < AmstradGraphics.ColorCount; ++candidate) {
      var entry = candidate * 3;
      if (entry + 2 >= palette.Length)
        break;

      long cost = 0;
      for (var y = y0; y < y0 + height; ++y)
      for (var x = x0; x < x0 + width; ++x) {
        var at = (y * Width + x) * 3;
        long dr = rgb[at] - palette[entry], dg = rgb[at + 1] - palette[entry + 1], db = rgb[at + 2] - palette[entry + 2];
        cost += dr * dr + dg * dg + db * db;
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }

  /// <summary>A second colour for the row, chosen from what the first one leaves worst served.</summary>
  private static int _SecondNearest(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> palette, int y) {
    var first = _Nearest(rgb, palette, 0, y, Width, 1) * 3;
    var best = 0;
    var bestCost = long.MinValue;

    for (var x = 0; x < Width; ++x) {
      var at = (y * Width + x) * 3;
      long dr = rgb[at] - palette[first], dg = rgb[at + 1] - palette[first + 1], db = rgb[at + 2] - palette[first + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost <= bestCost)
        continue;

      bestCost = cost;
      best = x;
    }

    var worst = (y * Width + best) * 3;

    return _NearestToColor(palette, rgb[worst], rgb[worst + 1], rgb[worst + 2]);
  }

  private static int _NearestToColor(ReadOnlySpan<byte> palette, int red, int green, int blue) {
    var best = 0;
    var bestCost = long.MaxValue;

    for (var candidate = 0; candidate < AmstradGraphics.ColorCount; ++candidate) {
      var entry = candidate * 3;
      if (entry + 2 >= palette.Length)
        break;

      long dr = red - palette[entry], dg = green - palette[entry + 1], db = blue - palette[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }
}
