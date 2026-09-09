using System;
using FileFormat.Core;

namespace FileFormat.SuperHiresEditor;

/// <summary>In-memory representation of a Super Hires Editor (.she) logo for the Commodore 64.</summary>
/// <remarks>
/// A logo editor and not a picture editor: 96 by 88, which is twelve character cells across and
/// eleven down, with eight sprites over the top 84 rows in two layers of four. The sprite layers get
/// one colour each for the whole logo; everything the sprites do not cover is an ordinary
/// high-resolution screen, two colours to a cell.
/// <para/>
/// The file is 3250 bytes: the bitmap at 2, the video matrix at 1058, 2048 bytes of sprite across
/// 1190 — the back layer's four blocks then the front layer's four, for each of the four sprite rows
/// — and the two sprite colours at 3238 and 3239.
/// <para/>
/// What was written before was 18002 bytes of two whole 320 by 200 high-resolution screens, which is
/// not this format's size, geometry or structure. Nothing in it was sprite at all.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct SuperHiresEditorFile
  : IImageFormatReader<SuperHiresEditorFile>, IImageToRawImage<SuperHiresEditorFile>,
    IImageFromRawImage<SuperHiresEditorFile>, IImageFormatWriter<SuperHiresEditorFile> {

  static string IImageFormatMetadata<SuperHiresEditorFile>.PrimaryExtension => ".she";
  static string[] IImageFormatMetadata<SuperHiresEditorFile>.FileExtensions => [".she"];
  static SuperHiresEditorFile IImageFormatReader<SuperHiresEditorFile>.FromSpan(ReadOnlySpan<byte> data) => SuperHiresEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<SuperHiresEditorFile>.ToBytes(SuperHiresEditorFile file) => SuperHiresEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SuperHiresEditorFile>.VideoModes => [
    new("Super Hires Editor", [(ImageWidth, ImageHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Image width in pixels, always 96: twelve character cells.</summary>
  public const int ImageWidth = 96;

  /// <summary>Image height in pixels, always 88: eleven character cells.</summary>
  public const int ImageHeight = 88;

  /// <summary>Character cells across.</summary>
  internal const int Columns = ImageWidth / 8;

  /// <summary>Character cells down.</summary>
  internal const int Rows = ImageHeight / Commodore64Graphics.CellHeight;

  /// <summary>Rows the sprites reach: four rows of sprites at 21 raster lines apiece.</summary>
  internal const int SpriteRows = SuperHiresLayout.SpriteHeight * 4;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Where the bitmap starts.</summary>
  internal const int BitmapOffset = LoadAddressSize;

  /// <summary>Bytes the bitmap takes.</summary>
  internal const int BitmapSize = Columns * Rows * Commodore64Graphics.CellHeight;

  /// <summary>Where the video matrix starts, two colours to a cell.</summary>
  internal const int ScreenOffset = BitmapOffset + BitmapSize;

  /// <summary>Entries the video matrix holds.</summary>
  internal const int ScreenSize = Columns * Rows;

  /// <summary>Where the back sprite layer starts.</summary>
  internal const int BackSpritesOffset = ScreenOffset + ScreenSize;

  /// <summary>Where the front sprite layer starts: four sprite blocks past the back one.</summary>
  internal const int FrontSpritesOffset = BackSpritesOffset + 4 * SuperHiresLayout.SpriteStride;

  /// <summary>Bytes both sprite layers take together, interleaved four blocks at a time.</summary>
  internal const int SpriteAreaSize = 32 * SuperHiresLayout.SpriteStride;

  /// <summary>Where the colour of the back sprite layer sits.</summary>
  internal const int BackColorOffset = BackSpritesOffset + SpriteAreaSize;

  /// <summary>Where the colour of the front sprite layer sits.</summary>
  internal const int FrontColorOffset = BackColorOffset + 1;

  /// <summary>The length of a whole logo, which is also what identifies it.</summary>
  public const int FileSize = 3250;

  /// <summary>Default load address, which puts the bitmap at $2000.</summary>
  internal const ushort DefaultLoadAddress = 0x2000;

  /// <summary>Image width, always 96.</summary>
  public int Width => ImageWidth;

  /// <summary>Image height, always 88.</summary>
  public int Height => ImageHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The bitmap, one bit a pixel, a cell at a time.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The video matrix, two colours to a cell, foreground in the high nibble.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Both sprite layers, the back one's four blocks then the front one's, four rows over.</summary>
  public byte[] Sprites { get; init; }

  /// <summary>The colour the back sprite layer shows.</summary>
  public byte BackSpriteColor { get; init; }

  /// <summary>The colour the front sprite layer shows.</summary>
  public byte FrontSpriteColor { get; init; }

  /// <summary>Whatever the ten bytes after the two sprite colours held.</summary>
  public byte[] Trailer { get; init; }

  /// <summary>Converts this logo to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(SuperHiresEditorFile file) {
    var bitmap = file.BitmapData ?? [];
    var screen = file.ScreenData ?? [];
    var sprites = file.Sprites ?? [];
    var indices = new byte[ImageWidth * ImageHeight];

    for (var y = 0; y < ImageHeight; ++y)
    for (var x = 0; x < ImageWidth; ++x) {
      var bit = ~x & 7;
      int colour;

      if (y < SpriteRows && _SpriteBit(sprites, FrontSpritesOffset - BackSpritesOffset, x, y, bit))
        colour = file.FrontSpriteColor;
      else if (y < SpriteRows && _SpriteBit(sprites, 0, x, y, bit))
        colour = file.BackSpriteColor;
      else {
        var cell = y / Commodore64Graphics.CellHeight * Columns + x / 8;
        var lit = bitmap[cell * Commodore64Graphics.CellHeight + y % Commodore64Graphics.CellHeight] >> bit & 1;
        colour = screen[cell] >> (lit << 2);
      }

      indices[y * ImageWidth + x] = (byte)(colour & 0x0F);
    }

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  /// <summary>
  /// Whether one layer's sprite covers a pixel.
  /// </summary>
  /// <remarks>
  /// Eight sprite blocks to a row of them and four rows, but only four of the eight are one layer's:
  /// the back layer takes the first four of every row and the front layer the next four. So the two
  /// layers are four blocks apart in the same area, not two halves of it.
  /// </remarks>
  private static bool _SpriteBit(ReadOnlySpan<byte> sprites, int layer, int x, int y, int bit) {
    var at = layer + SuperHiresLayout.SpriteOffset(x, y, 3);

    return at < sprites.Length && (sprites[at] >> bit & 1) != 0;
  }

  /// <summary>Encodes a picture as a Super Hires Editor logo, scaling it to 96x88 first.</summary>
  /// <remarks>
  /// The sprites are left blank and the whole logo drawn in the bitmap, which is two colours a cell.
  /// Deciding what belongs on a sprite layer — one colour apiece for the whole logo, over an area
  /// eight sprites can cover — is a placement problem of its own, and a bad attempt at it would cost
  /// the cells their second colour for nothing.
  /// </remarks>
  public static SuperHiresEditorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(ImageWidth, ImageHeight).PixelData;
    var bitmap = new byte[BitmapSize];
    var screen = new byte[ScreenSize];
    _EncodeHires(rgb, bitmap, screen);

    return new() {
      LoadAddress = DefaultLoadAddress,
      BitmapData = bitmap,
      ScreenData = screen,
      Sprites = new byte[SpriteAreaSize],
      Trailer = new byte[FileSize - FrontColorOffset - 1],
    };
  }

  /// <summary>
  /// Packs a twelve-cell screen: for each cell, the two colours that describe it with least error.
  /// </summary>
  /// <remarks>
  /// The shared high-resolution encoder assumes the forty cells of a whole screen, and a logo is
  /// twelve — every cell but the first row's would land in the wrong place.
  /// </remarks>
  private static void _EncodeHires(ReadOnlySpan<byte> rgb, Span<byte> bitmap, Span<byte> screen) {
    Span<int> cell = stackalloc int[Commodore64Graphics.CellHeight * 8];

    for (var top = 0; top < ImageHeight; top += Commodore64Graphics.CellHeight)
    for (var left = 0; left < ImageWidth; left += 8) {
      for (var y = 0; y < Commodore64Graphics.CellHeight; ++y)
      for (var x = 0; x < 8; ++x) {
        var at = ((top + y) * ImageWidth + left + x) * 3;
        cell[y * 8 + x] = Commodore64Graphics.FindNearestColorIndex(rgb[at], rgb[at + 1], rgb[at + 2]);
      }

      var foreground = 0;
      var background = 0;
      var bestError = long.MaxValue;
      for (var first = 0; first < Commodore64Graphics.ColorCount; ++first)
      for (var second = 0; second <= first; ++second) {
        long error = 0;
        foreach (var index in cell)
          error += Math.Min(_Distance(index, first), _Distance(index, second));

        if (error >= bestError)
          continue;

        bestError = error;
        foreground = first;
        background = second;
      }

      var at2 = top / Commodore64Graphics.CellHeight * Columns + left / 8;
      for (var y = 0; y < Commodore64Graphics.CellHeight; ++y) {
        var row = 0;
        for (var x = 0; x < 8; ++x)
          if (_Distance(cell[y * 8 + x], foreground) <= _Distance(cell[y * 8 + x], background))
            row |= 1 << (7 - x);

        bitmap[at2 * Commodore64Graphics.CellHeight + y] = (byte)row;
      }

      screen[at2] = (byte)(foreground << 4 | background);
    }
  }

  private static int _Distance(int left, int right) {
    if (left == right)
      return 0;

    int a = Commodore64Graphics.HexColors[left], b = Commodore64Graphics.HexColors[right];
    int dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
    int dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
    int db = (a & 0xFF) - (b & 0xFF);

    return dr * dr + dg * dg + db * db;
  }
}
