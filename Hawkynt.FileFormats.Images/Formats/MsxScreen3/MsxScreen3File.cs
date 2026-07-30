using System;
using FileFormat.Core;

namespace FileFormat.MsxScreen3;

/// <summary>In-memory representation of an MSX Screen 3 picture (.sc3).</summary>
/// <remarks>
/// The MSX's low-resolution mode: 64x48 blocks of four by four pixels, each free to be any of the
/// sixteen colours. It is the same hardware as Screen 2 used differently — the blocks are patterns,
/// two colours to a byte — so a whole screen costs 1536 bytes and has none of the cell constraint
/// the character modes impose. What it gives up is everything finer than four pixels.
/// <para/>
/// Short files repeat one row of patterns across the screen instead of naming them per cell, which
/// is what a program did when it never redefined the screen map.
/// </remarks>
[FormatMagicBytes([0xFE])]
public readonly record struct MsxScreen3File
  : IImageFormatReader<MsxScreen3File>, IImageToRawImage<MsxScreen3File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 256;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Screen pixels a block spans, both ways.</summary>
  public const int BlockSize = 4;

  /// <summary>Offset of the pattern table, after the BSAVE header.</summary>
  public const int PatternOffset = MsxGraphics.BsaveHeaderSize;

  /// <summary>Offset of the screen map in video memory.</summary>
  public const int ScreenMapOffset = 0x0800;

  /// <summary>Offset of a stored MSX2 palette in video memory.</summary>
  public const int PaletteOffset = 0x2020;

  /// <summary>Offset of the sprite attributes in video memory.</summary>
  public const int SpriteAttributeOffset = 0x1B00;

  /// <summary>Offset of the sprite patterns in video memory.</summary>
  public const int SpritePatternOffset = 0x3800;

  /// <summary>Smallest file that carries a screen map rather than repeating one row.</summary>
  public const int LongFileSize = 2823;

  /// <summary>Size of a file that carries the sprite plane as well.</summary>
  public const int SpriteFileSize = 16391;

  /// <summary>Smallest file the mode can be read from.</summary>
  public const int MinimumFileSize = 1543;

  static string IImageFormatMetadata<MsxScreen3File>.PrimaryExtension => ".sc3";
  static string[] IImageFormatMetadata<MsxScreen3File>.FileExtensions => [".sc3"];
  static MsxScreen3File IImageFormatReader<MsxScreen3File>.FromSpan(ReadOnlySpan<byte> data)
    => MsxScreen3Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MsxScreen3File>.VideoModes => [
    new("Screen 3", [(Width, Height)], [16])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(MsxScreen3File file) {
    var data = file.Data ?? [];
    var hasScreenMap = data.Length >= LongFileSize;
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      // Without a screen map the patterns repeat: the cell's own position names it.
      var cell = hasScreenMap
        ? _At(data, PatternOffset + ScreenMapOffset + ((y & ~7) << 2) + (x >> 3))
        : (y & 224) + (x >> 3);

      // Eight bytes to a pattern, one per four scanlines, two blocks to a byte.
      var at = PatternOffset + (cell << 3) + ((y >> 2) & 7);
      pixels[y * Width + x] = (byte)((_At(data, at) >> (~x & 4)) & 15);
    }

    if (data.Length == SpriteFileSize)
      MsxGraphics.OverlaySprites(
        data, PatternOffset + SpriteAttributeOffset, PatternOffset + SpritePatternOffset, 3, pixels, Width, Height);

    var stored = MsxGraphics.HasPaletteAt(data, PatternOffset + PaletteOffset);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = stored
        ? MsxGraphics.PaletteToRgb(data.AsSpan(PatternOffset + PaletteOffset), 16)
        : MsxGraphics.Tms9918Palette.ToArray(),
      PaletteCount = 16,
    };
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
