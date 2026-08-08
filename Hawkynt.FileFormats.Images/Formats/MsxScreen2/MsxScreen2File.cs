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
[FormatMagicBytes([0xFE])]
public sealed class MsxScreen2File
  : IImageFormatReader<MsxScreen2File>, IImageToRawImage<MsxScreen2File>,
    IImageFromRawImage<MsxScreen2File>, IImageFormatWriter<MsxScreen2File> {

  static string IImageFormatMetadata<MsxScreen2File>.PrimaryExtension => ".sc2";
  static string[] IImageFormatMetadata<MsxScreen2File>.FileExtensions => [".sc2", ".grp"];
  static MsxScreen2File IImageFormatReader<MsxScreen2File>.FromSpan(ReadOnlySpan<byte> data) => MsxScreen2Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MsxScreen2File>.VideoModes => [
    new("Default", [(FixedWidth, FixedHeight)], [16])
  ];

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

  /// <summary>Builds a Screen 2 image from a <see cref="RawImage"/>. Every pixel is mapped onto the
  /// TMS9918's fixed sixteen colours. The name table is filled so every one of the 768 cells addresses
  /// its own, otherwise-unused pattern and colour slot (position within its bank) — nothing is shared,
  /// so nothing is lost to pattern reuse. Within each cell, every 8x1 pixel row gets its own foreground
  /// and background, since that is what Screen 2's per-row colour table actually offers; only the two
  /// most common colours per row survive.</summary>
  public static MsxScreen2File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"MSX Screen 2 images are always {FixedWidth}x{FixedHeight}, but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, MsxGraphics.Tms9918Palette.ToArray());
    var patternGenerator = new byte[PatternGeneratorSize];
    var colorTable = new byte[ColorTableSize];
    var patternNameTable = new byte[PatternNameTableSize];

    Span<int> rowFreq = stackalloc int[16];
    for (var charRow = 0; charRow < 24; ++charRow)
    for (var charCol = 0; charCol < 32; ++charCol) {
      var bank = charRow / 8;
      var charIndex = (charRow % 8) * 32 + charCol;
      patternNameTable[charRow * 32 + charCol] = (byte)charIndex;
      var slotOffset = bank * 2048 + charIndex * 8;

      for (var pixelRow = 0; pixelRow < 8; ++pixelRow) {
        var y = charRow * 8 + pixelRow;

        rowFreq.Clear();
        for (var bit = 0; bit < 8; ++bit) {
          var x = charCol * 8 + bit;
          ++rowFreq[indexed.PixelData[y * FixedWidth + x]];
        }

        int fg = 0, bg = 0, best1 = -1, best2 = -1;
        for (var c = 0; c < 16; ++c) {
          if (rowFreq[c] > best1) {
            best2 = best1; bg = fg;
            best1 = rowFreq[c]; fg = c;
          } else if (rowFreq[c] > best2) {
            best2 = rowFreq[c]; bg = c;
          }
        }

        byte patternByte = 0;
        for (var bit = 0; bit < 8; ++bit) {
          var x = charCol * 8 + bit;
          if (indexed.PixelData[y * FixedWidth + x] == fg)
            patternByte |= (byte)(1 << (7 - bit));
        }

        patternGenerator[slotOffset + pixelRow] = patternByte;
        colorTable[slotOffset + pixelRow] = (byte)((fg << 4) | bg);
      }
    }

    return new() {
      PatternGenerator = patternGenerator, ColorTable = colorTable,
      PatternNameTable = patternNameTable, HasBsaveHeader = false,
    };
  }

}
