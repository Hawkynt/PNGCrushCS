using System;
using FileFormat.Core;

namespace FileFormat.TobiasRichterSlideshow;

/// <summary>In-memory representation of a Tobias Richter Fullscreen Slideshow picture (.pci).</summary>
/// <remarks>
/// An overscanned ST picture: 352 by 278, wider and taller than the machine's nominal screen,
/// stored as two fields that alternate and with a fresh sixteen-colour palette for every one of the
/// 556 scanlines. The planes are not interleaved by word as an ST picture normally is but stored
/// one after another, each 12232 bytes, which is what a display list that reloads the palette every
/// line needs.
/// </remarks>
public readonly record struct TobiasRichterSlideshowFile
  : IImageFormatReader<TobiasRichterSlideshowFile>, IImageToRawImage<TobiasRichterSlideshowFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 352;

  /// <summary>Rows in one field.</summary>
  public const int Height = 278;

  /// <summary>Bitplanes a pixel is built from.</summary>
  public const int Bitplanes = 4;

  /// <summary>Bytes one row of one plane occupies.</summary>
  public const int BytesPerPlaneRow = Width / 8;

  /// <summary>Bytes one whole plane occupies.</summary>
  public const int BytesPerPlane = BytesPerPlaneRow * Height;

  /// <summary>Where the second field's planes start.</summary>
  public const int SecondFieldOffset = BytesPerPlane * Bitplanes;

  /// <summary>Where the per-scanline palettes start.</summary>
  public const int PaletteOffset = SecondFieldOffset * 2;

  /// <summary>Colours a scanline's palette holds.</summary>
  public const int ColorCount = 16;

  /// <summary>Scanlines with a palette of their own: both fields, one after the other.</summary>
  public const int PaletteLineCount = Height * 2;

  /// <summary>Total file size.</summary>
  public const int FileSize = PaletteOffset + PaletteLineCount * ColorCount * AtariStGraphics.PaletteEntrySize;

  static string IImageFormatMetadata<TobiasRichterSlideshowFile>.PrimaryExtension => ".pci";
  static string[] IImageFormatMetadata<TobiasRichterSlideshowFile>.FileExtensions => [".pci"];
  static TobiasRichterSlideshowFile IImageFormatReader<TobiasRichterSlideshowFile>.FromSpan(ReadOnlySpan<byte> data)
    => TobiasRichterSlideshowReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<TobiasRichterSlideshowFile>.VideoModes => [
    new("Atari ST overscan", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The whole file, every area of which is at an absolute offset.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(TobiasRichterSlideshowFile file) {
    var data = file.Data ?? [];

    // Which form the palettes are in is settled once from all of them, not line by line.
    var ste = AtariStGraphics.IsStePalette(data, PaletteOffset, PaletteLineCount * ColorCount);
    var fields = new byte[2][];

    for (var field = 0; field < 2; ++field) {
      var rgb = new byte[Width * Height * 3];
      var planeOffset = SecondFieldOffset * field;

      for (var y = 0; y < Height; ++y) {
        var line = field * Height + y;
        var palette = AtariStGraphics.ReadPalette(
          data, PaletteOffset + line * ColorCount * AtariStGraphics.PaletteEntrySize, ColorCount, ste);

        for (var x = 0; x < Width; ++x) {
          var entry = _PlanePixel(data, planeOffset + (x >> 3), x) * 3;
          var target = (y * Width + x) * 3;
          rgb[target] = palette[entry];
          rgb[target + 1] = palette[entry + 1];
          rgb[target + 2] = palette[entry + 2];
        }

        planeOffset += BytesPerPlaneRow;
      }

      fields[field] = rgb;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(fields[0], fields[1]),
    };
  }

  /// <summary>Reads one pixel from planes that are whole-picture blocks rather than interleaved.</summary>
  private static int _PlanePixel(ReadOnlySpan<byte> data, int offset, int x) {
    var bit = ~x & 7;
    var index = 0;
    for (var plane = Bitplanes; --plane >= 0;) {
      var at = offset + plane * BytesPerPlane;
      index = (index << 1) | (at < data.Length ? (data[at] >> bit) & 1 : 0);
    }

    return index;
  }
}
