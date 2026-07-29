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
  : IImageFormatReader<BotticelliFile>, IImageToRawImage<BotticelliFile> {

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
}
