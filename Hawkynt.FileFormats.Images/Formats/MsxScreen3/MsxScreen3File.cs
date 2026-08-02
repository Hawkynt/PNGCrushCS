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
// The byte 0xFE opens every BSAVE file the MSX writes, whichever screen mode it holds, so it says
// what the container is and nothing about which of these formats this is. Nine of them declared it
// as their magic, and the registry consults magic before extension — so whichever it happened to
// reach first took every MSX picture. A Screen 5 file, 256 by 212, was being opened as a Screen 6
// one and drawn 512 by 424. The extension is what tells these apart, and it is what decides now.
public readonly record struct MsxScreen3File
  : IImageFormatReader<MsxScreen3File>, IImageToRawImage<MsxScreen3File>,
    IImageFromRawImage<MsxScreen3File>, IImageFormatWriter<MsxScreen3File> {

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

  /// <summary>Bytes the screen map occupies: thirty-two cells across, twenty-four down.</summary>
  public const int ScreenMapSize = 32 * 24;

  /// <summary>Smallest file the mode can be read from.</summary>
  public const int MinimumFileSize = 1543;

  static string IImageFormatMetadata<MsxScreen3File>.PrimaryExtension => ".sc3";
  static string[] IImageFormatMetadata<MsxScreen3File>.FileExtensions => [".sc3"];
  static MsxScreen3File IImageFormatReader<MsxScreen3File>.FromSpan(ReadOnlySpan<byte> data)
    => MsxScreen3Reader.FromSpan(data);
  static byte[] IImageFormatWriter<MsxScreen3File>.ToBytes(MsxScreen3File file)
    => MsxScreen3Writer.ToBytes(file);
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

  /// <summary>Builds a screen of four-by-four blocks, each one of the chip's sixteen colours.</summary>
  /// <remarks>
  /// The awkward part is the pattern table, which holds 256 patterns of eight bytes for 768 cells —
  /// far too few to give each cell its own. It works out because a cell only ever uses two of its
  /// pattern's eight bytes, and which two depends on the cell's row: rows cycle through the four
  /// byte-pairs, so four cell rows can share one pattern without ever colliding.
  /// <para/>
  /// Naming the pattern <c>(row / 4) * 32 + column</c> makes that sharing exact: cells that share a
  /// pattern are always four rows apart, which is precisely the spacing that puts them in different
  /// bytes. It needs 192 of the 256 patterns and no cell compromises with another.
  /// </remarks>
  public static MsxScreen3File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height);
    var palette = MsxGraphics.Tms9918Palette;
    var data = new byte[LongFileSize];

    for (var row = 0; row < 24; ++row)
    for (var column = 0; column < 32; ++column) {
      var pattern = row / 4 * 32 + column;
      data[PatternOffset + ScreenMapOffset + row * 32 + column] = (byte)pattern;

      // Two bytes a cell, one per band of four scanlines, and two blocks packed into each.
      for (var band = 0; band < 2; ++band) {
        var top = row * 8 + band * 4;
        var left = _Block(rgb.PixelData, palette, column * 8, top);
        var right = _Block(rgb.PixelData, palette, column * 8 + 4, top);

        data[PatternOffset + (pattern << 3) + ((row * 2 + band) & 7)] = (byte)((left << 4) | right);
      }
    }

    return new() { Data = data };
  }

  /// <summary>The colour closest to the average of one four-by-four block.</summary>
  private static int _Block(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> palette, int left, int top) {
    long red = 0, green = 0, blue = 0;
    for (var y = 0; y < BlockSize; ++y)
    for (var x = 0; x < BlockSize; ++x) {
      var at = ((top + y) * Width + left + x) * 3;
      red += rgb[at];
      green += rgb[at + 1];
      blue += rgb[at + 2];
    }

    const int pixels = BlockSize * BlockSize;
    red /= pixels;
    green /= pixels;
    blue /= pixels;

    var best = 1;
    var bestCost = long.MaxValue;

    // Entry 0 is transparent rather than a colour, so it is not a candidate.
    for (var entry = 1; entry < 16; ++entry) {
      long dr = red - palette[entry * 3], dg = green - palette[entry * 3 + 1], db = blue - palette[entry * 3 + 2];
      var cost = dr * dr * 77 + dg * dg * 150 + db * db * 29;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = entry;
    }

    return best;
  }
}
