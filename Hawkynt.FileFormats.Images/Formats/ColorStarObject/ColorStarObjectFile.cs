using System;
using FileFormat.Core;

namespace FileFormat.ColorStarObject;

/// <summary>In-memory representation of a ColorSTar object (.obj).</summary>
/// <remarks>
/// A clipping from a ColorSTar drawing, in one of two shapes. The monochrome one is a four-byte
/// header and a single bitplane; the colour one begins with sixteen palette entries written as
/// decimal text, one per line, and then a header and four bitplanes. Both store their dimensions
/// one less than they are, so a one-pixel object is not an empty one.
/// <para/>
/// The extension is shared with MonoSTar's objects, which are a different format, so content
/// decides.
/// </remarks>
public readonly record struct ColorStarObjectFile
  : IImageFormatReader<ColorStarObjectFile>, IImageToRawImage<ColorStarObjectFile> {

  static string IImageFormatMetadata<ColorStarObjectFile>.PrimaryExtension => ".obj";
  static string[] IImageFormatMetadata<ColorStarObjectFile>.FileExtensions => [".obj"];
  static ColorStarObjectFile IImageFormatReader<ColorStarObjectFile>.FromSpan(ReadOnlySpan<byte> data)
    => ColorStarObjectReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ColorStarObjectFile>.VideoModes => [
    new("Atari ST", [(IntegerRange.Any, IntegerRange.Any)], [2, 16])
  ];

  /// <summary>The whole file, which the bitmap offset points into.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Where the bitplanes start.</summary>
  public int BitmapOffset { get; init; }

  /// <summary>Bitplanes a pixel is built from: one, or four.</summary>
  public int Bitplanes { get; init; }

  /// <summary>Sixteen RGB triplets, or the two of a monochrome object.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(ColorStarObjectFile file) {
    var data = file.Data ?? [];
    var palette = file.Palette ?? [];
    var stride = (file.Width + 15) >> 4 << 1;
    if (file.Bitplanes > 1)
      stride *= file.Bitplanes;

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = AtariStGraphics.UnpackBitplanes(
        data, file.BitmapOffset, stride, file.Bitplanes, file.Width, file.Height),
      Palette = palette,
      PaletteCount = palette.Length / 3,
    };
  }
}
