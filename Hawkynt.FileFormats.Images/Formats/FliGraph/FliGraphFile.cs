using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.FliGraph;

/// <summary>In-memory representation of an FLI Graph picture for the Commodore 64.</summary>
/// <remarks>
/// This wanted 17474 bytes and read them as a bitmap, a block of screens and colour memory laid end
/// to end. Nothing in an FLI Graph is laid end to end: every block takes a whole page of address
/// space for the thousand bytes it uses, and the file is 17409. Every sample was refused.
/// <para/>
/// The order is colour memory first, then the eight video matrices, then the bitmap — which is the
/// opposite end of the file from where it was being looked for. That is what makes it FLI: the
/// machine is pointed at a different matrix on each raster line of a character cell, so every row
/// chooses its own colours rather than the cell choosing once for all eight.
/// <para/>
/// It is multicolour, two bits a pixel, so the picture is 148 across drawn at 296. The leftmost
/// three character cells cannot be coloured in time and are not part of it.
/// </remarks>
public readonly record struct FliGraphFile : IImageFormatReader<FliGraphFile>, IImageToRawImage<FliGraphFile>, IImageFormatWriter<FliGraphFile> {

  static string IImageFormatMetadata<FliGraphFile>.PrimaryExtension => ".flg";
  static string[] IImageFormatMetadata<FliGraphFile>.FileExtensions => [".flg", ".bml", ".fli"];
  static FliGraphFile IImageFormatReader<FliGraphFile>.FromSpan(ReadOnlySpan<byte> data) => FliGraphReader.FromSpan(data);
  static byte[] IImageFormatWriter<FliGraphFile>.ToBytes(FliGraphFile file) => FliGraphWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<FliGraphFile>.VideoModes => [
    new("FLI Graph", [(VisibleWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>Pixels a row holds once drawn, each stored one twice.</summary>
  public const int FixedWidth = 320;

  /// <summary>Pixels across the picture: the first three cells cannot be coloured in time.</summary>
  public const int VisibleWidth = 296;

  /// <summary>
  /// How many stored pixels are hidden at the left.
  /// </summary>
  /// <remarks>
  /// Twenty-four drawn pixels, and a stored one is drawn twice, so twelve of them — not the six a
  /// count of four-pixel cells would give.
  /// </remarks>
  internal const int HiddenStoredPixels = (FixedWidth - VisibleWidth) / 2;

  /// <summary>Rows.</summary>
  public const int FixedHeight = 200;

  /// <summary>Character columns held in memory.</summary>
  internal const int Columns = FixedWidth / 8;

  internal const int LoadAddressSize = 2;

  /// <summary>The entries a matrix or colour memory holds.</summary>
  internal const int BankSize = 1000;

  /// <summary>The address space one of those takes: a whole page.</summary>
  internal const int BankStride = 1024;

  /// <summary>How many matrices, one for each raster line of a cell.</summary>
  internal const int ScreenBankCount = 8;

  internal const int BitmapDataSize = 8000;

  /// <summary>Colour memory comes first, right after the load address.</summary>
  internal const int ColorRamOffset = LoadAddressSize;

  /// <summary>The matrices follow it, a page apart.</summary>
  internal const int ScreensOffset = ColorRamOffset + BankStride;

  /// <summary>And the bitmap follows all eight of those.</summary>
  internal const int BitmapOffset = ScreensOffset + ScreenBankCount * BankStride;

  /// <summary>The least a whole picture takes: 2 + 1024 + 8 x 1024 + 8000.</summary>
  public const int MinimumFileSize = BitmapOffset + BitmapDataSize;

  /// <summary>
  /// What pattern 00 shows.
  /// </summary>
  /// <remarks>
  /// Black in every sample, and the file states it nowhere that changing alters the picture.
  /// </remarks>
  internal const int Background = 0;

  /// <summary>Always 296.</summary>
  public int Width => VisibleWidth;

  /// <summary>Always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The bitmap, two bits a pixel.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The eight video matrices, a thousand entries apiece.</summary>
  public byte[] Screens { get; init; }

  /// <summary>Colour memory, which pattern 11 takes and which every row shares.</summary>
  public byte[] ColorRam { get; init; }

  /// <summary>Converts this picture to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(FliGraphFile file) {
    var bitmap = file.BitmapData ?? [];
    var screens = file.Screens ?? [];
    var colorRam = file.ColorRam ?? [];
    var stored = VisibleWidth / 2;
    var indices = new byte[VisibleWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
      for (var sx = 0; sx < stored; ++sx) {
        var column = sx + HiddenStoredPixels;
        var cell = y / 8 * Columns + column / 4;
        var pattern = (bitmap[cell * 8 + y % 8] >> ((3 - column % 4) * 2)) & 3;

        // Which matrix speaks for this row is the whole of what FLI is.
        var entry = screens[y % ScreenBankCount * BankSize + cell];
        var index = pattern switch {
          0 => Background,
          1 => entry >> 4,
          2 => entry & 0x0F,
          _ => colorRam[cell] & 0x0F,
        };

        // Two bits a pixel, so each stored pixel is drawn twice.
        indices[y * VisibleWidth + sx * 2] = (byte)index;
        indices[y * VisibleWidth + sx * 2 + 1] = (byte)index;
      }

    return new() {
      Width = VisibleWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }
}
