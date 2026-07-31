using System;
using FileFormat.Core;

namespace FileFormat.ExtendSuperHires;

/// <summary>In-memory representation of an Extend Super Hires Interlace Editor picture (.esh).</summary>
/// <remarks>
/// Two C64 hires screens shown alternately and averaged, each with a band of sprites over it. Both
/// share one colour map and one set of sprite colours: interlacing on this machine buys the
/// mixtures between colours the cells already have, so duplicating the colour data would cost half
/// the file for nothing.
/// <para/>
/// A sprite covers three character columns and twenty-one rows, and the picture is 192 by 200 —
/// eight sprites across and just under ten down, which is what a C64 can display without the
/// multiplexing that would cost the processor.
/// </remarks>
public readonly record struct ExtendSuperHiresFile
  : IImageFormatReader<ExtendSuperHiresFile>, IImageToRawImage<ExtendSuperHiresFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 192;

  /// <summary>Rows.</summary>
  public const int Height = 200;

  /// <summary>Offset of the first frame's bitmap.</summary>
  public const int FirstBitmapOffset = 3;

  /// <summary>Offset of the second frame's bitmap.</summary>
  public const int SecondBitmapOffset = 4803;

  /// <summary>Offset of the first frame's sprites.</summary>
  public const int FirstSpriteOffset = 9603;

  /// <summary>Offset of the second frame's sprites.</summary>
  public const int SecondSpriteOffset = 14723;

  /// <summary>Offset of the colour map both frames share.</summary>
  public const int ColorMapOffset = 19843;

  /// <summary>Offset of the sprite colours, one per three columns.</summary>
  public const int SpriteColorsOffset = 20443;

  /// <summary>Size of a file that carries the picture outright.</summary>
  public const int UnpackedFileSize = 20454;

  /// <summary>Size a packed file unpacks to.</summary>
  public const int UnpackedSize = 20452;

  static string IImageFormatMetadata<ExtendSuperHiresFile>.PrimaryExtension => ".esh";
  static string[] IImageFormatMetadata<ExtendSuperHiresFile>.FileExtensions => [".esh"];
  static ExtendSuperHiresFile IImageFormatReader<ExtendSuperHiresFile>.FromSpan(ReadOnlySpan<byte> data)
    => ExtendSuperHiresReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ExtendSuperHiresFile>.VideoModes => [
    new("Extend Super Hires", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The picture, unpacked if it was packed.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(ExtendSuperHiresFile file) {
    var data = file.Data ?? [];
    var palette = Commodore64Graphics.CreatePalette();

    var first = _Render(data, FirstBitmapOffset, FirstSpriteOffset, palette);
    var second = _Render(data, SecondBitmapOffset, SecondSpriteOffset, palette);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = FrameBlend.Average(first, second),
    };
  }

  private static byte[] _Render(ReadOnlySpan<byte> data, int bitmap, int sprites, ReadOnlySpan<byte> palette) {
    var rgb = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var bit = ~x & 7;
      var column = x >> 3;

      int color;
      var sprite = sprites + (((y / 21 << 3) + column / 3) << 6) + y % 21 * 3 + column % 3;

      if (((_At(data, sprite) >> bit) & 1) != 0)
        color = _At(data, SpriteColorsOffset + column / 3);
      else {
        // The colour map holds both of a cell's colours in one byte; the bitmap picks the nibble.
        var offset = (y & ~7) * 3 + column;
        color = _At(data, ColorMapOffset + offset)
                >> (((_At(data, bitmap + (offset << 3) + (y & 7)) >> bit) & 1) << 2);
      }

      var entry = (color & 15) * 3;
      var target = (y * Width + x) * 3;
      rgb[target] = palette[entry];
      rgb[target + 1] = palette[entry + 1];
      rgb[target + 2] = palette[entry + 2];
    }

    return rgb;
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
