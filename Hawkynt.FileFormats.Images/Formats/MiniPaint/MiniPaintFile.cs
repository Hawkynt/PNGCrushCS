using System;
using FileFormat.Core;

namespace FileFormat.MiniPaint;

/// <summary>In-memory representation of a MINIPAINT picture (.mg).</summary>
/// <remarks>
/// A VIC-20 screen that mixes its two graphics modes cell by cell. Each sixteen-pixel-wide cell
/// carries a colour nibble, and the top bit of that nibble decides how the cell's bitmap is read:
/// set means two bits a pixel against four colours at half the horizontal resolution, clear means
/// one bit a pixel against two at full. So a picture can spend detail where it has edges and colour
/// where it has areas, which is more than either mode offers alone.
/// <para/>
/// The bitmap runs column by column — a whole column of 192 rows before the next — which is the
/// order a redefined character set occupies memory. A separate bit inverts the two-colour cells,
/// so the same bitmap can read either way round.
/// </remarks>
public readonly record struct MiniPaintFile
  : IImageFormatReader<MiniPaintFile>, IImageToRawImage<MiniPaintFile>,
    IImageFromRawImage<MiniPaintFile>, IImageFormatWriter<MiniPaintFile> {

  static byte[] IImageFormatWriter<MiniPaintFile>.ToBytes(MiniPaintFile file) => MiniPaintWriter.ToBytes(file);

  /// <summary>Pixels across.</summary>
  public const int Width = 160;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>
  /// The BASIC stub every file starts with, which loads and runs the picture and is what identifies
  /// the format — there is no signature of its own.
  /// </summary>
  public static ReadOnlySpan<byte> Signature => [
    241, 16, 12, 18, 216, 7, 158, 32, (byte)'8', (byte)'5', (byte)'8', (byte)'4', 0, 0, 0,
  ];

  /// <summary>Offset of the byte holding the colour the two-colour cells draw their ink from.</summary>
  public const int InkOffset = 15;

  /// <summary>Offset of the byte holding the background, the border and the inversion bit.</summary>
  public const int ControlOffset = 16;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 17;

  /// <summary>Offset of the per-cell colour nibbles.</summary>
  public const int ColorsOffset = 3857;

  /// <summary>Total file size.</summary>
  public const int FileSize = 4097;

  static string IImageFormatMetadata<MiniPaintFile>.PrimaryExtension => ".mg";
  static string[] IImageFormatMetadata<MiniPaintFile>.FileExtensions => [".mg"];
  static MiniPaintFile IImageFormatReader<MiniPaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => MiniPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MiniPaintFile>.VideoModes => [
    new("MINIPAINT", [(Width, Height)], [Vic20Graphics.ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(MiniPaintFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    Span<byte> colors = stackalloc byte[4];
    colors[0] = (byte)(data[ControlOffset] >> 4);
    colors[1] = (byte)(data[ControlOffset] & 7);
    colors[3] = (byte)(data[InkOffset] >> 4);

    // One bit of the control byte says which way round the two-colour cells read.
    var invert = ~(data[ControlOffset] >> 3) & 1;

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var cell = data[ColorsOffset + (y >> 4) * 10 + (x >> 4)];
      var color = (cell >> ((x >> 1) & 4)) & 15;
      var bits = data[BitmapOffset + (x >> 3) * Height + y];

      int index;
      if (color >= 8) {
        colors[2] = (byte)(color & 7);
        index = (bits >> (~x & 6)) & 3;
      } else {
        colors[2] = (byte)color;
        index = (((bits >> (~x & 7)) & 1) ^ invert) << 1;
      }

      pixels[y * Width + x] = colors[index];
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Vic20Graphics.CreatePalette(),
      PaletteCount = Vic20Graphics.ColorCount,
    };
  }

  /// <summary>Pixels across a colour area, which is half of what the cell byte covers.</summary>
  private const int _AreaWidth = 8;

  /// <summary>Rows down a colour area.</summary>
  private const int _AreaHeight = 16;

  /// <summary>Builds a picture in the four-colour mode, which is the one that uses the whole palette.</summary>
  /// <remarks>
  /// Every eight-by-sixteen area chooses one colour of its own; three more are shared by the whole
  /// picture, and two of those come from a register with only eight values rather than sixteen. The
  /// shared three are picked first from a reduction of the whole picture, and each area then takes
  /// whichever of the eight it can have suits its own pixels best.
  /// <para/>
  /// Areas can also be written in a two-colour mode that halves the horizontal detail for a colour
  /// less; this always takes the four-colour one, which every area can use.
  /// </remarks>
  public static MiniPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var vic = Vic20Graphics.CreatePalette();

    var quantized = ColorQuantizer.Quantize(
      PixelConverter.Convert(image.SampleTo(Width, Height), PixelFormat.Bgra32).PixelData,
      Width * Height, 4);

    // Index 1 comes from a three-bit register, so it is the one confined to the first eight colours.
    var background = _Nearest(vic, 16, quantized.Palette, 0);
    var auxiliary = _Nearest(vic, 8, quantized.Palette, 1);
    var ink = _Nearest(vic, 16, quantized.Palette, 2);

    var data = new byte[FileSize];
    Signature.CopyTo(data.AsSpan(0));
    data[InkOffset] = (byte)(ink << 4);

    // The high nibble is the background, the low three bits the auxiliary, and bit 3 says which way
    // round a two-colour area reads — irrelevant here, since none is written.
    data[ControlOffset] = (byte)((background << 4) | auxiliary);

    Span<byte> colors = stackalloc byte[4];
    colors[0] = background;
    colors[1] = auxiliary;
    colors[3] = ink;

    for (var areaY = 0; areaY < Height / _AreaHeight; ++areaY)
    for (var areaX = 0; areaX < Width / _AreaWidth; ++areaX) {
      var x0 = areaX * _AreaWidth;
      var y0 = areaY * _AreaHeight;

      var own = _ChooseAreaColor(rgb, vic, colors, x0, y0);
      colors[2] = own;

      // Two areas share a cell byte: the left one takes the low nibble, the right one the high.
      var cell = ColorsOffset + areaY * 10 + (x0 >> 4);
      data[cell] |= (byte)((own | 8) << ((areaX & 1) << 2));

      for (var y = y0; y < y0 + _AreaHeight; ++y)
      for (var x = x0; x < x0 + _AreaWidth; x += 2) {
        var index = _NearestOfFour(rgb, vic, colors, x, y);
        data[BitmapOffset + (x >> 3) * Height + y] |= (byte)(index << (~x & 6));
      }
    }

    return new() { Data = data };
  }

  /// <summary>The colour an area should own, given the three it has to share.</summary>
  private static byte _ChooseAreaColor(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> vic, Span<byte> colors, int x0, int y0) {
    byte best = 0;
    var bestCost = long.MaxValue;

    Span<byte> trial = stackalloc byte[4];
    colors.CopyTo(trial);

    for (byte candidate = 0; candidate < 8; ++candidate) {
      trial[2] = candidate;
      long cost = 0;

      for (var y = y0; y < y0 + _AreaHeight; ++y)
      for (var x = x0; x < x0 + _AreaWidth; x += 2)
        cost += _PairCost(rgb, vic, trial, x, y);

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }

  /// <summary>Which of the four colours a pixel pair should take.</summary>
  private static int _NearestOfFour(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> vic, ReadOnlySpan<byte> colors, int x, int y) {
    var (red, green, blue) = _PairAverage(rgb, x, y);
    var best = 0;
    var bestCost = long.MaxValue;

    for (var i = 0; i < 4; ++i) {
      var entry = colors[i] * 3;
      long dr = red - vic[entry], dg = green - vic[entry + 1], db = blue - vic[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = i;
    }

    return best;
  }

  /// <summary>How far the best of the four sits from a pixel pair.</summary>
  private static long _PairCost(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> vic, ReadOnlySpan<byte> colors, int x, int y) {
    var (red, green, blue) = _PairAverage(rgb, x, y);
    var bestCost = long.MaxValue;

    for (var i = 0; i < 4; ++i) {
      var entry = colors[i] * 3;
      long dr = red - vic[entry], dg = green - vic[entry + 1], db = blue - vic[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost < bestCost)
        bestCost = cost;
    }

    return bestCost;
  }

  /// <summary>The mean of the two pixels one stored value covers.</summary>
  private static (int Red, int Green, int Blue) _PairAverage(ReadOnlySpan<byte> rgb, int x, int y) {
    var left = (y * Width + x) * 3;
    var right = left + 3;

    return (
      (rgb[left] + rgb[right]) >> 1,
      (rgb[left + 1] + rgb[right + 1]) >> 1,
      (rgb[left + 2] + rgb[right + 2]) >> 1);
  }

  /// <summary>The machine colour nearest one entry of a reduction, within however many are allowed.</summary>
  private static byte _Nearest(ReadOnlySpan<byte> vic, int available, ReadOnlySpan<byte> palette, int index) {
    int red = palette[index * 3], green = palette[index * 3 + 1], blue = palette[index * 3 + 2];
    byte best = 0;
    var bestCost = int.MaxValue;

    for (var candidate = 0; candidate < available; ++candidate) {
      var entry = candidate * 3;
      int dr = red - vic[entry], dg = green - vic[entry + 1], db = blue - vic[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (byte)candidate;
    }

    return best;
  }
}
