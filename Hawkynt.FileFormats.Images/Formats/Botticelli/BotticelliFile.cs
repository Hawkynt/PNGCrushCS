using System;
using FileFormat.Core;

namespace FileFormat.Botticelli;

/// <summary>In-memory representation of a Botticelli picture (.p4i) for the Commodore 16 and Plus/4.</summary>
/// <remarks>
/// A Plus/4 screen keeps the two halves of a TED colour apart: a luminance area and a hue area, each
/// one byte per character cell, with the bitmap after them. A cell therefore picks its colours out of
/// the full 121 the chip can make rather than out of a sixteen-entry palette.
/// <para/>
/// One extension covers three different pictures. The 10050-byte files are full screens and carry a
/// <c>MULT</c> marker when they are multicolour; a 2050-byte file is the startup logo, which has no
/// colour areas at all and draws from four fixed colours.
/// </remarks>
public readonly record struct BotticelliFile
  : IImageFormatReader<BotticelliFile>, IImageToRawImage<BotticelliFile>,
    IImageFromRawImage<BotticelliFile>, IImageFormatWriter<BotticelliFile> {

  /// <summary>Size of a full screen.</summary>
  public const int ScreenFileSize = 10050;

  /// <summary>Size of the startup logo.</summary>
  public const int LogoFileSize = 2050;

  /// <summary>Offset of the marker that tells the two screen kinds apart.</summary>
  public const int MarkerOffset = 1020;

  /// <summary>The marker a multicolour screen carries.</summary>
  public static ReadOnlySpan<byte> MulticolorMarker => "MULT"u8;

  /// <summary>Offset of the luminance area, one byte per cell.</summary>
  public const int LuminanceOffset = 2;

  /// <summary>Offset of the two shared background registers.</summary>
  public const int BackgroundOffset = 1024;

  /// <summary>Offset of the hue area, one byte per cell.</summary>
  public const int HueOffset = 1026;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 2050;

  /// <summary>Offset of the logo's bitmap.</summary>
  public const int LogoBitmapOffset = 2;

  /// <summary>Displayed width of a screen; multicolour halves the horizontal resolution but not the width.</summary>
  public const int ScreenWidth = 320;

  /// <summary>Displayed height of a screen.</summary>
  public const int ScreenHeight = 200;

  /// <summary>Displayed width of the logo.</summary>
  public const int LogoWidth = 256;

  /// <summary>Displayed height of the logo.</summary>
  public const int LogoHeight = 64;

  /// <summary>Character cells across a screen.</summary>
  public const int Columns = ScreenWidth / 8;

  /// <summary>The four colours the logo draws with, as TED colour indices.</summary>
  public static ReadOnlySpan<byte> LogoColors => [0, 49, 81, 113];

  static string IImageFormatMetadata<BotticelliFile>.PrimaryExtension => ".p4i";
  static string[] IImageFormatMetadata<BotticelliFile>.FileExtensions => [".p4i"];
  static BotticelliFile IImageFormatReader<BotticelliFile>.FromSpan(ReadOnlySpan<byte> data) => BotticelliReader.FromSpan(data);
  static byte[] IImageFormatWriter<BotticelliFile>.ToBytes(BotticelliFile file) => BotticelliWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<BotticelliFile>.VideoModes => [
    new("Botticelli", [(ScreenWidth, ScreenHeight)], [121]),
    new("Multi Botticelli", [(ScreenWidth, ScreenHeight)], [121]),
    new("Logo", [(LogoWidth, LogoHeight)], [4]),
  ];

  /// <summary>Which of the three pictures this is.</summary>
  public BotticelliMode Mode { get; init; }

  /// <summary>The file's bytes, kept whole because the areas are addressed by absolute offset.</summary>
  public byte[] Data { get; init; }

  /// <summary>Displayed width.</summary>
  public int Width => this.Mode == BotticelliMode.Logo ? LogoWidth : ScreenWidth;

  /// <summary>Displayed height.</summary>
  public int Height => this.Mode == BotticelliMode.Logo ? LogoHeight : ScreenHeight;

  /// <summary>
  /// Byte offset of a pixel's bitmap byte within the screen area. Rows within a character cell are
  /// consecutive, so a row of cells occupies 320 bytes and the row inside the cell is the low part.
  /// </summary>
  private static int _CellOffset(int x, int y) => (y & ~7) * Columns + (x & ~7) + (y & 7);

  public static RawImage ToRawImage(BotticelliFile file) {
    var data = file.Data ?? [];
    var width = file.Width;
    var height = file.Height;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = file.Mode switch {
        BotticelliMode.Logo => _LogoColor(data, x, y),
        BotticelliMode.Multicolor => _MulticolorColor(data, x, y),
        _ => _HiresColor(data, x, y),
      };

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore16Graphics.CreatePalette(),
      PaletteCount = Commodore16Graphics.ColorCount,
    };
  }

  private static byte _HiresColor(ReadOnlySpan<byte> data, int x, int y) {
    var offset = _CellOffset(x, y);
    var set = ((_At(data, BitmapOffset + offset) >> (~x & 7)) & 1) != 0;
    var cell = offset >> 3;

    // A set bit takes the cell's foreground, a clear one its background; each is a luminance from
    // one area and a hue from the other.
    return set
      ? _Color(_At(data, LuminanceOffset + cell) & 7, _At(data, HueOffset + cell) >> 4)
      : _Color(_At(data, LuminanceOffset + cell) >> 4, _At(data, HueOffset + cell) & 15);
  }

  private static byte _MulticolorColor(ReadOnlySpan<byte> data, int x, int y) {
    var offset = _CellOffset(x, y);
    var pattern = (_At(data, BitmapOffset + offset) >> (~x & 6)) & 3;
    var cell = offset >> 3;

    // Patterns 00 and 11 come from the two screen-wide background registers, 01 and 10 from the
    // cell's own pair — which is what limits a multicolour cell to two colours of its own.
    return pattern switch {
      0 => _Color(_At(data, BackgroundOffset + 1) & 7, _At(data, BackgroundOffset + 1) >> 4),
      1 => _Color(_At(data, LuminanceOffset + cell) & 7, _At(data, HueOffset + cell) >> 4),
      2 => _Color(_At(data, LuminanceOffset + cell) >> 4, _At(data, HueOffset + cell) & 15),
      _ => _Color(_At(data, BackgroundOffset) & 7, _At(data, BackgroundOffset) >> 4),
    };
  }

  private static byte _LogoColor(ReadOnlySpan<byte> data, int x, int y) {
    // The logo is stored column-of-cells first rather than row first.
    var b = _At(data, LogoBitmapOffset + ((x & ~7) << 3) + y);
    return LogoColors[(b >> (~x & 6)) & 3];
  }

  /// <summary>Combines a luminance and a hue into a palette index.</summary>
  private static byte _Color(int luminance, int hue) => (byte)Commodore16Graphics.ColorIndex(luminance, hue);

  private static byte _At(ReadOnlySpan<byte> data, int offset) => offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Fits a picture into two TED colours a cell.</summary>
  /// <remarks>
  /// A cell's two colours are chosen from what is in it: the most common colour and the one furthest
  /// from it, which between them span the cell better than the two most common would when a cell
  /// holds a gradient. Each is then split back into the luminance and hue the two areas hold.
  /// </remarks>
  public static BotticelliFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image
      .SampleTo(ScreenWidth, ScreenHeight)
      .EnsureIndexed(PixelFormat.Indexed8, Commodore16Graphics.CreatePalette());

    var data = new byte[ScreenFileSize];

    // Where a Plus/4 loads it. Nothing reads this, but a file without it is not one the machine
    // could have saved.
    data[0] = 0x00;
    data[1] = 0x18;

    Span<int> frequency = stackalloc int[Commodore16Graphics.ColorCount];
    var palette = Commodore16Graphics.CreatePalette();

    for (var cellY = 0; cellY < ScreenHeight / 8; ++cellY)
    for (var cellX = 0; cellX < Columns; ++cellX) {
      frequency.Clear();
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        ++frequency[indexed.PixelData[(cellY * 8 + y) * ScreenWidth + cellX * 8 + x]
          % Commodore16Graphics.ColorCount];

      var background = 0;
      for (var i = 1; i < Commodore16Graphics.ColorCount; ++i)
        if (frequency[i] > frequency[background])
          background = i;

      var foreground = background;
      var furthest = -1;
      for (var i = 0; i < Commodore16Graphics.ColorCount; ++i) {
        if (frequency[i] == 0)
          continue;

        var distance = _Distance(palette, i, background);
        if (distance <= furthest)
          continue;

        furthest = distance;
        foreground = i;
      }

      var cell = cellY * Columns + cellX;
      // Luminance is three bits and hue four; the foreground takes the low half of one byte and the
      // high half of the other, which is what puts a cell's two colours in two bytes rather than four.
      data[LuminanceOffset + cell] = (byte)(((background >> 4) << 4) | (foreground >> 4));
      data[HueOffset + cell] = (byte)(((foreground & 15) << 4) | (background & 15));

      for (var y = 0; y < 8; ++y) {
        byte bits = 0;
        for (var x = 0; x < 8; ++x) {
          var index = indexed.PixelData[(cellY * 8 + y) * ScreenWidth + cellX * 8 + x]
            % Commodore16Graphics.ColorCount;
          if (_Distance(palette, index, foreground) < _Distance(palette, index, background))
            bits |= (byte)(1 << (7 - x));
        }

        data[BitmapOffset + _CellOffset(cellX * 8, cellY * 8 + y)] = bits;
      }
    }

    return new() { Mode = BotticelliMode.Hires, Data = data };
  }

  private static int _Distance(ReadOnlySpan<byte> palette, int a, int b) {
    int dr = palette[a * 3] - palette[b * 3];
    int dg = palette[a * 3 + 1] - palette[b * 3 + 1];
    int db = palette[a * 3 + 2] - palette[b * 3 + 2];
    return dr * dr + dg * dg + db * db;
  }
}
