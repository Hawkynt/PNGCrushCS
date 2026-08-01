using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Picasso;

/// <summary>In-memory representation of a Picasso picture (.pic0, with a .pic1 beside it).</summary>
/// <remarks>
/// A VIC-20 multicolour screen of 176x176 in cells sixteen scanlines deep. The two files divide the
/// picture from its colours — the .pic0 holds the bitmap and the two screen-wide colours, the .pic1
/// the per-cell one — which is how the machine itself held them, in two areas of memory a program
/// could point the chip at independently.
/// <para/>
/// Every cell's colour byte must have its multicolour bit set, because a picture using both
/// character modes at once is not one this program made.
/// </remarks>
public readonly record struct PicassoFile
  : IImageFormatReader<PicassoFile>, IImageToRawImage<PicassoFile>,
    IImageFromRawImage<PicassoFile>, IImageFormatWriter<PicassoFile> {

  static byte[] IImageFormatWriter<PicassoFile>.ToBytes(PicassoFile file) => PicassoWriter.ToBytes(file);

  /// <summary>Pixels across and down.</summary>
  public const int Size = 176;

  /// <summary>Cells across.</summary>
  public const int Columns = Size / 8;

  /// <summary>Scanlines a cell spans.</summary>
  public const int CellHeight = 16;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 2;

  /// <summary>Offset of the byte holding the border and one of the shared colours.</summary>
  public const int AuxiliaryOffset = 3888;

  /// <summary>Offset of the byte holding the background and the other shared colour.</summary>
  public const int BackgroundOffset = 3889;

  /// <summary>Total size of the bitmap file.</summary>
  public const int FileSize = 3890;

  /// <summary>Size of the companion holding the per-cell colours.</summary>
  public const int ColorFileSize = 244;

  /// <summary>Offset of the per-cell colours within the companion.</summary>
  public const int ColorsOffset = 2;

  static string IImageFormatMetadata<PicassoFile>.PrimaryExtension => ".pic0";
  static string[] IImageFormatMetadata<PicassoFile>.FileExtensions => [".pic0"];
  static PicassoFile IImageFormatReader<PicassoFile>.FromSpan(ReadOnlySpan<byte> data)
    => PicassoReader.FromSpan(data);

  /// <summary>Reads the file together with the companion it cannot be shown without.</summary>
  static PicassoFile IImageFormatReader<PicassoFile>.FromFile(FileInfo file)
    => PicassoReader.FromFile(file);
  static VideoMode[] IImageFormatMetadata<PicassoFile>.VideoModes => [
    new("Picasso", [(Size, Size)], [Vic20Graphics.ColorCount])
  ];

  /// <summary>The bitmap file.</summary>
  public byte[] Data { get; init; }

  /// <summary>The per-cell colours from the companion file.</summary>
  public byte[] Colors { get; init; }

  public static RawImage ToRawImage(PicassoFile file) {
    var data = file.Data ?? [];
    var colors = file.Colors ?? [];
    var pixels = new byte[Size * Size];

    for (var y = 0; y < Size; ++y)
    for (var x = 0; x < Size; ++x) {
      var cell = (y / CellHeight) * Columns + (x >> 3);
      var ink = cell + ColorsOffset < colors.Length ? colors[cell + ColorsOffset] : 0;

      var at = BitmapOffset + (cell << 4) + (y & 15);
      var pattern = at < data.Length ? (data[at] >> (~x & 6)) & 3 : 0;

      pixels[y * Size + x] = (byte)(pattern switch {
        0 => data[BackgroundOffset] >> 4,
        1 => data[BackgroundOffset] & 7,
        2 => ink & 7,
        _ => data[AuxiliaryOffset] >> 4,
      });
    }

    return new() {
      Width = Size,
      Height = Size,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Vic20Graphics.CreatePalette(),
      PaletteCount = Vic20Graphics.ColorCount,
    };
  }

  /// <summary>The extension the cell colours live under, beside the picture.</summary>
  public const string CompanionExtension = ".pic1";

  /// <summary>Rows of cells down the picture.</summary>
  public const int CellRows = Size / CellHeight;

  /// <summary>Builds a picture: three colours shared by all of it, and one for each cell.</summary>
  /// <remarks>
  /// Two of the three shared colours come from registers with only eight values rather than sixteen,
  /// and so does every cell's own — so the four are not chosen from one set but from three. The
  /// cells go in a second file, which is why a picture written without one cannot be shown.
  /// </remarks>
  public static PicassoFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Size, Size).PixelData;
    var vic = Vic20Graphics.CreatePalette();

    var quantized = ColorQuantizer.Quantize(
      PixelConverter.Convert(image.SampleTo(Size, Size), PixelFormat.Bgra32).PixelData, Size * Size, 3);

    var background = _Nearest(vic, 16, quantized.Palette, 0);
    var border = _Nearest(vic, 8, quantized.Palette, 1);
    var auxiliary = _Nearest(vic, 16, quantized.Palette, 2);

    var data = new byte[FileSize];
    var colors = new byte[ColorFileSize];

    // The load address the picture was saved from, and three bytes of the program that came with
    // it. None of them is the picture, and a file without them is not recognised as one.
    data[0] = 0;
    data[1] = 13;
    data[3876] = 150;
    data[3877] = 23;
    data[3879] = 140;

    data[BackgroundOffset] = (byte)((background << 4) | border);
    data[AuxiliaryOffset] = (byte)(auxiliary << 4);

    Span<byte> four = [background, border, 0, auxiliary];

    for (var row = 0; row < CellRows; ++row)
    for (var column = 0; column < Columns; ++column) {
      var cell = row * Columns + column;
      int x0 = column * 8, y0 = row * CellHeight;

      var ink = _ChooseInk(rgb, vic, four, x0, y0);
      // The eighth bit is not part of the colour: it says the cell has one at all.
      colors[cell + ColorsOffset] = (byte)(ink | 8);
      four[2] = ink;

      for (var y = y0; y < y0 + CellHeight; ++y)
      for (var x = x0; x < x0 + 8; x += 2) {
        var pattern = _NearestOfFour(rgb, vic, four, x, y);
        data[BitmapOffset + (cell << 4) + (y & 15)] |= (byte)(pattern << (~x & 6));
      }
    }

    return new() { Data = data, Colors = colors };
  }

  /// <summary>Writes the colour file the picture cannot be shown without.</summary>
  static void IImageFormatWriter<PicassoFile>.WriteCompanions(PicassoFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);

    var colors = file.Colors ?? new byte[ColorFileSize];
    var padded = new byte[ColorFileSize];
    colors.AsSpan(0, Math.Min(colors.Length, ColorFileSize)).CopyTo(padded);

    File.WriteAllBytes(Path.ChangeExtension(target.FullName, CompanionExtension), padded);
  }

  /// <summary>The colour a cell should own, given the three it has to share.</summary>
  private static byte _ChooseInk(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> vic, Span<byte> four, int x0, int y0) {
    byte best = 0;
    var bestCost = long.MaxValue;

    Span<byte> trial = stackalloc byte[4];
    four.CopyTo(trial);

    for (byte candidate = 0; candidate < 8; ++candidate) {
      trial[2] = candidate;
      long cost = 0;

      for (var y = y0; y < y0 + CellHeight; ++y)
      for (var x = x0; x < x0 + 8; x += 2)
        cost += _PairCost(rgb, vic, trial, x, y);

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }

  private static int _NearestOfFour(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> vic, ReadOnlySpan<byte> four, int x, int y) {
    var (red, green, blue) = _PairAverage(rgb, x, y);
    var best = 0;
    var bestCost = long.MaxValue;

    for (var i = 0; i < 4; ++i) {
      var entry = four[i] * 3;
      long dr = red - vic[entry], dg = green - vic[entry + 1], db = blue - vic[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = i;
    }

    return best;
  }

  private static long _PairCost(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> vic, ReadOnlySpan<byte> four, int x, int y) {
    var (red, green, blue) = _PairAverage(rgb, x, y);
    var bestCost = long.MaxValue;

    for (var i = 0; i < 4; ++i) {
      var entry = four[i] * 3;
      long dr = red - vic[entry], dg = green - vic[entry + 1], db = blue - vic[entry + 2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost < bestCost)
        bestCost = cost;
    }

    return bestCost;
  }

  private static (int Red, int Green, int Blue) _PairAverage(ReadOnlySpan<byte> rgb, int x, int y) {
    var left = (y * Size + x) * 3;
    var right = left + 3;

    return (
      (rgb[left] + rgb[right]) >> 1,
      (rgb[left + 1] + rgb[right + 1]) >> 1,
      (rgb[left + 2] + rgb[right + 2]) >> 1);
  }

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
