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
[FormatMagicBytes([0xFE])]
public readonly record struct MsxScreen4File
  : IImageFormatReader<MsxScreen4File>, IImageToRawImage<MsxScreen4File> {

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
}
