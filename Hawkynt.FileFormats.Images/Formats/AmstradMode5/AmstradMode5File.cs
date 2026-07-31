using System;
using FileFormat.Core;

namespace FileFormat.AmstradMode5;

/// <summary>In-memory representation of a Mode 5 picture (.cm5, with a .gfx beside it).</summary>
/// <remarks>
/// The Amstrad has no mode 5. This is mode 1 — four colours, 288 pixels across — with the palette
/// rewritten every scanline, which is what the name refers to: the four colours become four per
/// row instead of four per screen, and one of them is rewritten six times across the row as well.
/// <para/>
/// So the two files divide by what changes and how often. The .gfx holds the bitmap, which does not
/// change; the .cm5 holds eight colour bytes per scanline, of which six belong to one of the four
/// pen values and let it vary across the width.
/// </remarks>
public readonly record struct AmstradMode5File
  : IImageFormatReader<AmstradMode5File>, IImageToRawImage<AmstradMode5File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 288;

  /// <summary>Rows.</summary>
  public const int Height = 256;

  /// <summary>Bytes one row of the bitmap occupies.</summary>
  public const int Stride = 72;

  /// <summary>Size of the file holding the colours.</summary>
  public const int FileSize = 2049;

  /// <summary>Size of the companion holding the bitmap.</summary>
  public const int BitmapFileSize = Stride * Height;

  /// <summary>Colour bytes each scanline carries.</summary>
  public const int ColorsPerRow = 8;

  /// <summary>Pixels one of the row's six varying colours covers.</summary>
  public const int ZoneWidth = 48;

  static string IImageFormatMetadata<AmstradMode5File>.PrimaryExtension => ".cm5";
  static string[] IImageFormatMetadata<AmstradMode5File>.FileExtensions => [".cm5"];
  static AmstradMode5File IImageFormatReader<AmstradMode5File>.FromSpan(ReadOnlySpan<byte> data)
    => AmstradMode5Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AmstradMode5File>.VideoModes => [
    new("Mode 5", [(Width, Height)], [AmstradGraphics.ColorCount])
  ];

  /// <summary>The colours, eight per scanline.</summary>
  public byte[] Colors { get; init; }

  /// <summary>The bitmap from the companion file.</summary>
  public byte[] Bitmap { get; init; }

  public static RawImage ToRawImage(AmstradMode5File file) {
    var colors = file.Colors ?? [];
    var bitmap = file.Bitmap ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = y * Stride + (x >> 2);
      var b = at < bitmap.Length ? bitmap[at] : 0;

      // Mode 1 interleaves its two bits across the byte the same way mode 0 does its four.
      var pen = (b >> (~x & 3)) & 17;

      var slot = pen switch {
        0 => 3 + (y * ColorsPerRow) + x / ZoneWidth,
        1 => 1 + (y * ColorsPerRow),
        16 => 2 + (y * ColorsPerRow),
        _ => 0,
      };

      var c = slot < colors.Length ? colors[slot] : 0;
      pixels[y * Width + x] = (byte)(c - AmstradGraphics.ColorBias);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = AmstradGraphics.Palette.ToArray(),
      PaletteCount = AmstradGraphics.ColorCount,
    };
  }
}
