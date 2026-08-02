using System;
using FileFormat.Core;

namespace FileFormat.MsxScreen4;

/// <summary>In-memory representation of an MSX Screen 4 picture (.sc4).</summary>
/// <remarks>
/// Screen 2's picture on Screen 2's tables, but on a V9938 rather than a TMS9918. The bitmap is
/// laid out identically; what changes is everything around it. The sixteen colours come from a
/// palette the machine can set rather than from the chip, so a file that stores none means the
/// MSX2's startup palette and not the TMS9918's fixed one. The sprites move to a different corner
/// of video memory and gain per-row colours.
/// </remarks>
// The byte 0xFE opens every BSAVE file the MSX writes, whichever screen mode it holds, so it says
// what the container is and nothing about which of these formats this is. Nine of them declared it
// as their magic, and the registry consults magic before extension — so whichever it happened to
// reach first took every MSX picture. A Screen 5 file, 256 by 212, was being opened as a Screen 6
// one and drawn 512 by 424. The extension is what tells these apart, and it is what decides now.
public readonly record struct MsxScreen4File
  : IImageFormatReader<MsxScreen4File>, IImageToRawImage<MsxScreen4File>,
    IImageFromRawImage<MsxScreen4File>, IImageFormatWriter<MsxScreen4File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 256;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Offset of video memory, after the BSAVE header.</summary>
  public const int VramOffset = MsxGraphics.BsaveHeaderSize;

  /// <summary>Offset of the pattern generator in video memory.</summary>
  public const int PatternOffset = 0x0000;

  /// <summary>Offset of the screen map in video memory.</summary>
  public const int ScreenMapOffset = 0x1800;

  /// <summary>Offset of the colour table in video memory.</summary>
  public const int ColorTableOffset = 0x2000;

  /// <summary>Offset of a stored palette in video memory.</summary>
  public const int PaletteOffset = 0x1B80;

  /// <summary>Offset of the sprite attributes in video memory.</summary>
  public const int SpriteAttributeOffset = 0x1E00;

  /// <summary>Offset of the sprite patterns in video memory.</summary>
  public const int SpritePatternOffset = 0x3800;

  /// <summary>Video memory a picture occupies.</summary>
  public const int VramSize = ColorTableOffset + 0x1800;

  /// <summary>Smallest file the mode can be read from.</summary>
  public const int MinimumFileSize = VramOffset + VramSize;

  /// <summary>Smallest file that carries the sprite plane as well.</summary>
  public const int SpriteFileSize = VramOffset + SpritePatternOffset + 0x0800;

  static string IImageFormatMetadata<MsxScreen4File>.PrimaryExtension => ".sc4";
  static string[] IImageFormatMetadata<MsxScreen4File>.FileExtensions => [".sc4"];
  static MsxScreen4File IImageFormatReader<MsxScreen4File>.FromSpan(ReadOnlySpan<byte> data)
    => MsxScreen4Reader.FromSpan(data);
  static byte[] IImageFormatWriter<MsxScreen4File>.ToBytes(MsxScreen4File file)
    => MsxScreen4Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MsxScreen4File>.VideoModes => [
    new("Screen 4", [(Width, Height)], [16])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(MsxScreen4File file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      // The three tables each split into three banks of 256 patterns, one per third of the screen.
      var bank = (y & 192) << 5;
      var pattern = _At(data, VramOffset + ScreenMapOffset + ((y & ~7) << 2) + (x >> 3));
      var at = bank + (pattern << 3) + (y & 7);

      var bits = _At(data, VramOffset + PatternOffset + at);
      var colors = _At(data, VramOffset + ColorTableOffset + at);
      pixels[y * Width + x] = (byte)(((bits >> (~x & 7)) & 1) == 0 ? colors & 15 : colors >> 4);
    }

    if (data.Length >= SpriteFileSize)
      MsxGraphics.OverlaySprites(
        data, VramOffset + SpriteAttributeOffset, VramOffset + SpritePatternOffset, 4, pixels, Width, Height);

    var stored = MsxGraphics.HasPaletteAt(data, VramOffset + PaletteOffset);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = MsxGraphics.PaletteToRgb(
        stored ? data.AsSpan(VramOffset + PaletteOffset) : MsxGraphics.DefaultPalette, 16),
      PaletteCount = 16,
    };
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Builds a screen, choosing two of the chip's colours for every row of every cell.</summary>
  /// <remarks>
  /// The same shape as Screen 2: three banks of 256 patterns against 768 cells, which is exactly
  /// one pattern per cell, so every cell is given its own and none has to compromise with another.
  /// Each of a cell's eight rows carries its own pair, so the choice is made per row over all 120
  /// of them.
  /// </remarks>
  public static MsxScreen4File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var palette = MsxGraphics.PaletteToRgb(MsxGraphics.DefaultPalette, 16);
    var data = new byte[MinimumFileSize];

    for (var cellRow = 0; cellRow < 24; ++cellRow)
    for (var column = 0; column < 32; ++column) {
      var pattern = cellRow % 8 * 32 + column;
      data[VramOffset + ScreenMapOffset + cellRow * 32 + column] = (byte)pattern;

      var bank = cellRow / 8 * 2048;
      for (var row = 0; row < 8; ++row) {
        var (foreground, background, bits) = _ChooseRow(rgb.PixelData, palette, column * 8, cellRow * 8 + row);
        var at = bank + (pattern << 3) + row;

        data[VramOffset + PatternOffset + at] = bits;
        data[VramOffset + ColorTableOffset + at] = (byte)((foreground << 4) | background);
      }
    }

    return new() { Data = data };
  }

  /// <summary>The two colours that describe one row of eight pixels with the least total error.</summary>
  private static (int Foreground, int Background, byte Bits) _ChooseRow(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> palette, int left, int y) {
    int bestForeground = 15, bestBackground = 1, bestBits = 0;
    var bestCost = long.MaxValue;

    for (var foreground = 0; foreground < 16; ++foreground)
    for (var background = 0; background <= foreground; ++background) {
      var cost = 0L;
      var bits = 0;

      for (var x = 0; x < 8; ++x) {
        var at = (y * Width + left + x) * 3;
        var toForeground = _Distance(rgb, at, palette, foreground);
        var toBackground = _Distance(rgb, at, palette, background);

        if (toForeground <= toBackground) {
          bits |= 1 << (7 - x);
          cost += toForeground;
        } else
          cost += toBackground;
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      bestForeground = foreground;
      bestBackground = background;
      bestBits = bits;
    }

    return (bestForeground, bestBackground, (byte)bestBits);
  }

  /// <summary>How far a pixel is from a palette entry, weighted the way the eye weights it.</summary>
  private static long _Distance(ReadOnlySpan<byte> rgb, int pixel, ReadOnlySpan<byte> palette, int entry) {
    long dr = rgb[pixel] - palette[entry * 3];
    long dg = rgb[pixel + 1] - palette[entry * 3 + 1];
    long db = rgb[pixel + 2] - palette[entry * 3 + 2];

    return dr * dr * 77 + dg * dg * 150 + db * db * 29;
  }
}
