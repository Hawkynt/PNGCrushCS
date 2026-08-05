using System;
using FileFormat.Core;

namespace FileFormat.Afli;

/// <summary>In-memory representation of an AFLI (Advanced FLI) hires image for the Commodore 64.</summary>
/// <remarks>
/// This used to want 9218 bytes read as an ordinary high-resolution screen — one bitmap and one
/// video matrix — which is not FLI at all and is not the length of any AFLI file. The only sample is
/// 16385 bytes and was refused outright.
/// <para/>
/// What makes it FLI is eight video matrices rather than one: the machine is made to point at a
/// different matrix on each of the eight raster lines of a character cell, so every row of a cell
/// chooses its own two colours instead of the cell choosing once for all eight. That costs eight
/// kilobytes and buys eight times the colour resolution down the screen.
/// <para/>
/// It also costs the left of the screen. The switch happens while the raster is still in the border
/// and cannot be ready before the first three character cells are drawn, so the leftmost 24 pixels
/// of every row are whatever the hardware was showing. They are not part of the picture and are not
/// returned: the picture is 296 across, which is what RECOIL draws.
/// </remarks>
public readonly record struct AfliFile : IImageFormatReader<AfliFile>, IImageToRawImage<AfliFile>, IImageFormatWriter<AfliFile> {

  static string IImageFormatMetadata<AfliFile>.PrimaryExtension => ".afl";
  static string[] IImageFormatMetadata<AfliFile>.FileExtensions => [".afl"];
  static AfliFile IImageFormatReader<AfliFile>.FromSpan(ReadOnlySpan<byte> data) => AfliReader.FromSpan(data);
  static byte[] IImageFormatWriter<AfliFile>.ToBytes(AfliFile file) => AfliWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AfliFile>.VideoModes => [
    new("AFLI", [(VisibleWidth, FixedHeight)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The pixels a row holds, the first three cells of which are not part of the picture.</summary>
  public const int FixedWidth = 320;

  /// <summary>Pixels across the picture, the hardware being unable to colour the first 24 of a row.</summary>
  public const int VisibleWidth = 296;

  /// <summary>Where the picture starts within a row.</summary>
  internal const int HiddenColumns = FixedWidth - VisibleWidth;

  /// <summary>Rows.</summary>
  public const int FixedHeight = 200;

  /// <summary>How many video matrices an AFLI carries, one for each raster line of a cell.</summary>
  internal const int ScreenCount = 8;

  /// <summary>The bytes one video matrix takes: a whole page for the thousand it uses.</summary>
  internal const int ScreenStride = 1024;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Size of the bitmap.</summary>
  internal const int BitmapDataSize = 8000;

  /// <summary>Where the matrices start.</summary>
  internal const int ScreensOffset = LoadAddressSize;

  /// <summary>Where the bitmap starts: after all eight matrices.</summary>
  internal const int BitmapOffset = ScreensOffset + ScreenCount * ScreenStride;

  /// <summary>The least a whole AFLI takes; a file may run on to the end of its 16K block.</summary>
  public const int MinimumFileSize = BitmapOffset + BitmapDataSize;

  /// <summary>Image width, always 296.</summary>
  public int Width => VisibleWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>The bitmap, eight thousand bytes, a cell at a time.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The eight video matrices, one after another, a whole page apiece.</summary>
  public byte[] Screens { get; init; }

  /// <summary>Converts this AFLI image to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(AfliFile file) {
    var bitmap = file.BitmapData ?? [];
    var screens = file.Screens ?? [];
    var indices = new byte[VisibleWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < VisibleWidth; ++x) {
        var column = x + HiddenColumns;
        var cell = y / 8 * (FixedWidth / 8) + column / 8;

        var pattern = bitmap[cell * 8 + y % 8];
        var lit = ((pattern >> (7 - column % 8)) & 1) != 0;

        // Which of the eight matrices speaks for this row is the whole of what FLI is.
        var entry = screens[y % ScreenCount * ScreenStride + cell];
        indices[y * VisibleWidth + x] = (byte)(lit ? entry >> 4 : entry & 0x0F);
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
