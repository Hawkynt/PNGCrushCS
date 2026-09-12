using System;
using FileFormat.Core;

namespace FileFormat.FliProfi;

/// <summary>In-memory representation of a FLI Profi picture for the Commodore 64.</summary>
/// <remarks>
/// A multicolour FLI screen that fills the left of the row rather than giving it up. FLI cannot
/// colour the first three character cells — the raster switch is not ready in time — so this covers
/// them with sprites, which the border does not stop, and the picture is the full 320 across
/// instead of the 296 the rest of the family shows.
/// <para/>
/// Everything is where the machine addresses it, and loading at $3780 says why those offsets: colour
/// memory lands at $3C00, the eight video matrices a page apart across $4000-$5FFF, and the bitmap
/// at $6000. Before that comes what the border needs — ten sprite blocks, a sprite colour for every
/// raster line, a colour for pattern 11 in the border for every raster line, and the two multicolour
/// registers the sprites share.
/// <para/>
/// What was written instead was 17002 bytes of bitmap, eight video matrices packed a thousand apart
/// and colour memory, with nothing for the border at all.
/// </remarks>
[VerifiedBy(ConformanceOracle.Recoil2Png)]
public readonly record struct FliProfiFile
  : IImageFormatReader<FliProfiFile>, IImageToRawImage<FliProfiFile>,
    IImageFromRawImage<FliProfiFile>, IImageFormatWriter<FliProfiFile> {

  static string IImageFormatMetadata<FliProfiFile>.PrimaryExtension => ".fpr";
  static string[] IImageFormatMetadata<FliProfiFile>.FileExtensions => [".fpr"];
  static FliProfiFile IImageFormatReader<FliProfiFile>.FromSpan(ReadOnlySpan<byte> data) => FliProfiReader.FromSpan(data);
  static byte[] IImageFormatWriter<FliProfiFile>.ToBytes(FliProfiFile file) => FliProfiWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FliProfiFile>.VideoModes => [
    new("FLI Profi", [(FixedWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The fixed width of the image in pixels: the whole row, sprites and all.</summary>
  public const int FixedWidth = Commodore64Fli.MemoryWidth;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = Commodore64Fli.ScreenHeight;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Where the sprite blocks that cover the left of every row start.</summary>
  internal const int SpritesOffset = 2;

  /// <summary>Sprite blocks: five down the screen, in two sets the display alternates between.</summary>
  internal const int SpriteBlockCount = 10;

  /// <summary>Bytes one sprite block takes.</summary>
  internal const int SpriteBlockSize = 64;

  /// <summary>Where the sprite colour for each raster line starts.</summary>
  internal const int SpriteColorsOffset = 642;

  /// <summary>Where the colour pattern 11 takes in the border, for each raster line, starts.</summary>
  internal const int BorderColorsOffset = 898;

  /// <summary>The first of the two multicolour registers the sprites share.</summary>
  internal const int FirstSpriteMulticolorOffset = 1098;

  /// <summary>The second of the two multicolour registers the sprites share.</summary>
  internal const int SecondSpriteMulticolorOffset = 1099;

  /// <summary>Where colour memory starts.</summary>
  internal const int ColorRamOffset = 1154;

  /// <summary>Where the video matrices start.</summary>
  internal const int MatricesOffset = 2178;

  /// <summary>Where the bitmap starts: after all eight matrices.</summary>
  internal const int BitmapOffset = MatricesOffset + Commodore64Fli.MatrixAreaSize;

  /// <summary>The length of a whole FLI Profi picture, which is also what identifies it.</summary>
  public const int FileSize = BitmapOffset + Commodore64Fli.BitmapSize;

  /// <summary>Default load address, which puts colour memory at $3C00 and the matrices at $4000.</summary>
  internal const ushort DefaultLoadAddress = 0x3780;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The sprite blocks that draw the leftmost three cells of every row.</summary>
  public byte[] Sprites { get; init; }

  /// <summary>The sprite's own colour, one entry for each raster line.</summary>
  public byte[] SpriteColors { get; init; }

  /// <summary>What pattern 11 shows in the border, in the high nibble, one entry a raster line.</summary>
  public byte[] BorderColors { get; init; }

  /// <summary>The first multicolour register the sprites share.</summary>
  public byte FirstSpriteMulticolor { get; init; }

  /// <summary>The second multicolour register the sprites share.</summary>
  public byte SecondSpriteMulticolor { get; init; }

  /// <summary>Colour memory, one entry a cell, which pattern 11 takes outside the border.</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>The eight video matrices, one after another, a whole page apiece.</summary>
  public byte[] Matrices { get; init; }

  /// <summary>The bitmap, eight thousand bytes, a cell at a time.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// Two decisions per pixel in the border and one outside it. The bitmap decides first everywhere;
  /// then, in the leftmost three cells, the sprite over that pixel overrides it wherever the sprite
  /// is not transparent.
  /// </remarks>
  public static RawImage ToRawImage(FliProfiFile file) {
    var bitmap = file.BitmapData ?? [];
    var matrices = file.Matrices ?? [];
    var colorRam = file.ColorRam ?? [];
    var sprites = file.Sprites ?? [];
    var spriteColors = file.SpriteColors ?? [];
    var borderColors = file.BorderColors ?? [];
    var indices = new byte[FixedWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
    for (var x = 0; x < FixedWidth; ++x) {
      var border = x < Commodore64Fli.HiddenColumns;
      var cell = y / Commodore64Graphics.CellHeight * Commodore64Graphics.Columns + x / 8;

      // Outside the border the matrix entry is the cell's; inside it there is none, and both nibbles
      // of the stand-in are 15, which is what the sprites are drawn over.
      var colour = border ? 0xFF : matrices[y % Commodore64Graphics.CellHeight * Commodore64Fli.MatrixStride + cell];

      colour = (bitmap[cell * Commodore64Graphics.CellHeight + y % Commodore64Graphics.CellHeight] >> (~x & 6) & 3) switch {
        0 => 0,
        1 => colour >> 4,
        3 => border ? borderColors[y] >> 4 : colorRam[cell],
        _ => colour,
      };

      if (border)
        colour = (sprites[_SpriteBlock(y) * SpriteBlockSize + (y >> 1) % 21 * 3 + (x >> 3)] >> (~x & 6) & 3) switch {
          1 => spriteColors[y],
          2 => file.FirstSpriteMulticolor,
          3 => file.SecondSpriteMulticolor,
          _ => colour,
        };

      indices[y * FixedWidth + x] = (byte)(colour & 0x0F);
    }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  /// <summary>Which sprite block covers a raster line: five down the screen, in two alternating sets.</summary>
  private static int _SpriteBlock(int y) => ((y + 1) & 2) != 0 ? 5 + y / 42 : y / 42;

  /// <summary>Encodes a picture as FLI Profi, scaling it to 320x200 first.</summary>
  /// <remarks>
  /// The sprites are left blank and the leftmost three cells encoded as pattern 00, which shows
  /// black. Drawing the border properly means solving a five-sprite multiplex with two shared
  /// colours and one more per raster line, which is a problem of its own and not one this attempts
  /// badly: what it writes is a correct FLI Profi with an empty border.
  /// </remarks>
  public static FliProfiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bitmap = new byte[Commodore64Fli.BitmapSize];
    var matrices = new byte[Commodore64Fli.MatrixAreaSize];
    var colorRam = new byte[Commodore64Fli.ColorRamSize];
    Commodore64Fli.EncodeMulticolor(
      _WithoutBorder(image), FixedHeight, 0, 0, bitmap, matrices, Commodore64Fli.MatrixStride, colorRam);

    return new() {
      LoadAddress = DefaultLoadAddress,
      Sprites = new byte[SpriteBlockCount * SpriteBlockSize],
      SpriteColors = new byte[FixedHeight],
      BorderColors = new byte[FixedHeight],
      ColorRam = colorRam,
      Matrices = matrices,
      BitmapData = bitmap,
    };
  }

  /// <summary>The part of the picture the bitmap draws, which is everything but the sprite border.</summary>
  private static RawImage _WithoutBorder(RawImage image) {
    var source = image.SampleTo(FixedWidth, FixedHeight);
    var rgb = new byte[Commodore64Fli.VisibleWidth * FixedHeight * 3];

    for (var y = 0; y < FixedHeight; ++y)
      source.PixelData
        .AsSpan((y * FixedWidth + Commodore64Fli.HiddenColumns) * 3, Commodore64Fli.VisibleWidth * 3)
        .CopyTo(rgb.AsSpan(y * Commodore64Fli.VisibleWidth * 3));

    return new() {
      Width = Commodore64Fli.VisibleWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }
}
