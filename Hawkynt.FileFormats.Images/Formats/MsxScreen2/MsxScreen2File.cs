using System;
using FileFormat.Core;

namespace FileFormat.MsxScreen2;

/// <summary>In-memory representation of an MSX Screen 2 (TMS9918) image (.sc2, .grp).</summary>
/// <remarks>
/// An optional seven-byte BSAVE header and then a copy of video memory. The three tables the mode
/// needs do not sit one after another in it: the patterns start at 0x0000, the screen map at
/// 0x1800 and the colours at 0x2000, with gaps between them that the sprite tables and anything
/// else the program left there occupy. So the file is 14336 bytes of video memory rather than the
/// 13056 the three tables add up to, and reading it as three consecutive blocks lands the last two
/// in the wrong place.
/// <para/>
/// A picture is 256x192 built from 768 eight-by-eight patterns, and the colour table gives each of
/// a pattern's eight rows its own foreground and background — which is what lets the mode look far
/// less blocky than a character screen and still cost a byte a cell to place.
/// </remarks>
// The byte 0xFE opens every BSAVE file the MSX writes, whichever screen mode it holds, so it says
// what the container is and nothing about which of these formats this is. Nine of them declared it
// as their magic, and the registry consults magic before extension — so whichever it happened to
// reach first took every MSX picture. A Screen 5 file, 256 by 212, was being opened as a Screen 6
// one and drawn 512 by 424. The extension is what tells these apart, and it is what decides now.
public sealed class MsxScreen2File : IImageFormatReader<MsxScreen2File>, IImageToRawImage<MsxScreen2File>, IImageFromRawImage<MsxScreen2File>, IImageFormatWriter<MsxScreen2File> {

  static string IImageFormatMetadata<MsxScreen2File>.PrimaryExtension => ".sc2";
  static string[] IImageFormatMetadata<MsxScreen2File>.FileExtensions => [".sc2", ".grp"];
  static MsxScreen2File IImageFormatReader<MsxScreen2File>.FromSpan(ReadOnlySpan<byte> data) => MsxScreen2Reader.FromSpan(data);

  static byte[] IImageFormatWriter<MsxScreen2File>.ToBytes(MsxScreen2File file) => MsxScreen2Writer.ToBytes(file);

  /// <summary>Fixed width of an MSX Screen 2 image.</summary>
  public const int FixedWidth = 256;

  /// <summary>Fixed height of an MSX Screen 2 image.</summary>
  public const int FixedHeight = 192;

  /// <summary>BSAVE header magic byte.</summary>
  public const byte BsaveMagic = 0xFE;

  /// <summary>BSAVE header size in bytes.</summary>
  public const int BsaveHeaderSize = 7;

  /// <summary>Size of the pattern generator table in bytes (3 banks x 2048).</summary>
  internal const int PatternGeneratorSize = 6144;

  /// <summary>Size of the color table in bytes (3 banks x 2048).</summary>
  internal const int ColorTableSize = 6144;

  /// <summary>Size of the pattern name table in bytes (32x24).</summary>
  internal const int PatternNameTableSize = 768;

  /// <summary>Where the pattern generator table sits in video memory.</summary>
  public const int PatternGeneratorOffset = 0x0000;

  /// <summary>Where the pattern name table sits in video memory.</summary>
  public const int PatternNameTableOffset = 0x1800;

  /// <summary>Where the colour table sits in video memory.</summary>
  public const int ColorTableOffset = 0x2000;

  /// <summary>Where a stored MSX2 palette sits, in the gap after the sprite attributes.</summary>
  public const int PaletteOffset = 0x1B80;

  /// <summary>Where the sprite attributes sit in video memory.</summary>
  public const int SpriteAttributeOffset = 0x1B00;

  /// <summary>Where the sprite patterns sit in video memory.</summary>
  public const int SpritePatternOffset = 0x3800;

  /// <summary>Video memory a file carrying the sprite plane holds.</summary>
  public const int SpriteVramSize = 0x4000;

  /// <summary>Total raw VRAM data size.</summary>
  public const int VramDataSize = ColorTableOffset + ColorTableSize;

  /// <summary>Total file size with BSAVE header.</summary>
  public const int FileWithHeaderSize = BsaveHeaderSize + VramDataSize;

  /// <summary>Image width, always 256.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 192.</summary>
  public int Height => FixedHeight;

  /// <summary>Pattern generator table (6144 bytes: 3 banks x 256 patterns x 8 bytes).</summary>
  public byte[] PatternGenerator { get; init; } = [];

  /// <summary>Color table (6144 bytes: 3 banks x 256 patterns x 8 bytes, high nib = fg, low nib = bg per row).</summary>
  public byte[] ColorTable { get; init; } = [];

  /// <summary>Pattern name table (768 bytes: 32x24 cell indices).</summary>
  public byte[] PatternNameTable { get; init; } = [];

  /// <summary>Whether the original data had a 7-byte BSAVE header.</summary>
  public bool HasBsaveHeader { get; init; }

  /// <summary>
  /// A stored MSX2 palette, or null when the file carried none and the chip's own colours apply.
  /// </summary>
  public byte[]? Palette { get; init; }

  /// <summary>
  /// Video memory as stored, kept only when the file reaches far enough to hold the sprite plane.
  /// </summary>
  /// <remarks>
  /// The three tables above are the picture; sprites are drawn over it from two more that sit
  /// elsewhere in memory, and the pattern one is past the end of a file that stops at the picture.
  /// </remarks>
  public byte[]? Vram { get; init; }

  /// <summary>Converts this MSX Screen 2 image to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(MsxScreen2File file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixels = new byte[FixedWidth * FixedHeight];

    for (var charRow = 0; charRow < 24; ++charRow)
      for (var charCol = 0; charCol < 32; ++charCol) {
        var charIndex = file.PatternNameTable[charRow * 32 + charCol];
        var bank = charRow / 8;
        var patternOffset = bank * 2048 + charIndex * 8;
        var colorOffset = bank * 2048 + charIndex * 8;

        for (var pixelRow = 0; pixelRow < 8; ++pixelRow) {
          var patternByte = patternOffset + pixelRow < file.PatternGenerator.Length
            ? file.PatternGenerator[patternOffset + pixelRow]
            : (byte)0;
          var colorByte = colorOffset + pixelRow < file.ColorTable.Length
            ? file.ColorTable[colorOffset + pixelRow]
            : (byte)0;
          var foreground = (colorByte >> 4) & 0x0F;
          var background = colorByte & 0x0F;
          var y = charRow * 8 + pixelRow;

          for (var bit = 0; bit < 8; ++bit) {
            var x = charCol * 8 + bit;
            var isSet = ((patternByte >> (7 - bit)) & 1) != 0;
            pixels[y * FixedWidth + x] = (byte)(isSet ? foreground : background);
          }
        }
      }

    if (file.Vram is { } vram)
      MsxGraphics.OverlaySprites(
        vram, SpriteAttributeOffset, SpritePatternOffset, 2, pixels, FixedWidth, FixedHeight);

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = file.Palette is { } stored ? MsxGraphics.PaletteToRgb(stored, 16) : MsxGraphics.Tms9918Palette.ToArray(),
      PaletteCount = 16,
    };
  }

  /// <summary>Builds a screen, choosing two of the chip's colours for every row of every cell.</summary>
  /// <remarks>
  /// The name table holds 768 cells and the three banks hold 256 patterns each — exactly one
  /// pattern per cell — so every cell can be given its own rather than sharing. That turns the
  /// encoding into an independent choice per cell and removes the pattern-reuse problem entirely.
  /// <para/>
  /// Within a cell the constraint is finer than a Spectrum's: each of the eight rows carries its
  /// own foreground and background, so a row of eight pixels may show two colours. The pair is
  /// chosen per row by trying all 120 of them, which is exact at that size.
  /// </remarks>
  public static MsxScreen2File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight);
    var palette = MsxGraphics.Tms9918Palette;

    var patterns = new byte[PatternGeneratorSize];
    var colors = new byte[ColorTableSize];
    var names = new byte[PatternNameTableSize];

    for (var cellRow = 0; cellRow < 24; ++cellRow)
    for (var cellColumn = 0; cellColumn < 32; ++cellColumn) {
      // Identity naming: cell n of a bank uses pattern n of that bank.
      var pattern = (cellRow % 8) * 32 + cellColumn;
      names[cellRow * 32 + cellColumn] = (byte)pattern;

      var at = cellRow / 8 * 2048 + pattern * 8;

      for (var row = 0; row < 8; ++row) {
        var y = cellRow * 8 + row;
        var (foreground, background, bits) = _ChooseRow(rgb.PixelData, palette, cellColumn * 8, y);

        patterns[at + row] = bits;
        colors[at + row] = (byte)((foreground << 4) | background);
      }
    }

    return new() {
      PatternGenerator = patterns,
      ColorTable = colors,
      PatternNameTable = names,
      HasBsaveHeader = true,
    };
  }

  /// <summary>The two colours that describe one row of eight pixels with the least total error.</summary>
  private static (int Foreground, int Background, byte Bits) _ChooseRow(
    ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> palette, int left, int y) {
    int bestForeground = 1, bestBackground = 1, bestBits = 0;
    var bestCost = long.MaxValue;

    for (var foreground = 0; foreground < 16; ++foreground)
    for (var background = 0; background <= foreground; ++background) {
      var cost = 0L;
      var bits = 0;

      for (var x = 0; x < 8; ++x) {
        var at = (y * FixedWidth + left + x) * 3;
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
