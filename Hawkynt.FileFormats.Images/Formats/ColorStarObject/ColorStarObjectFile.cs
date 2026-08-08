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
  : IImageFormatReader<ColorStarObjectFile>, IImageToRawImage<ColorStarObjectFile>,
    IImageFromRawImage<ColorStarObjectFile>, IImageFormatWriter<ColorStarObjectFile> {

  /// <summary>Colours a coloured object's palette names.</summary>
  public const int ColorCount = 16;

  /// <summary>The widest object the header's two bytes can state.</summary>
  public const int MaxWidth = 65536;

  /// <summary>The tallest object the header's one byte can state.</summary>
  public const int MaxHeight = 256;

  static string IImageFormatMetadata<ColorStarObjectFile>.PrimaryExtension => ".obj";
  static string[] IImageFormatMetadata<ColorStarObjectFile>.FileExtensions => [".obj"];
  static ColorStarObjectFile IImageFormatReader<ColorStarObjectFile>.FromSpan(ReadOnlySpan<byte> data)
    => ColorStarObjectReader.FromSpan(data);
  static byte[] IImageFormatWriter<ColorStarObjectFile>.ToBytes(ColorStarObjectFile file)
    => ColorStarObjectWriter.ToBytes(file);
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

  /// <summary>Encodes a clipping as the coloured form: sixteen colours over four bitplanes.</summary>
  /// <remarks>
  /// The monochrome form is not written. It is recognised by the two bytes a coloured object spends
  /// on its first palette entry being 0 and 1, so the two shapes are told apart by content and a
  /// coloured object is the one that can hold any picture.
  /// <para/>
  /// An object states its own size, so nothing is scaled for the sake of a screen. The header's
  /// fields do run out — two bytes across and one down — and a picture past them is brought to the
  /// largest the header can say rather than refused, since a clipping has no size of its own to
  /// betray.
  /// <para/>
  /// The palette is three bits a channel, written as a decimal number whose digits read as the three
  /// channels because the value packs them four bits apart.
  /// </remarks>
  public static ColorStarObjectFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Min(image.Width, MaxWidth);
    var height = Math.Min(image.Height, MaxHeight);
    var source = image.Width == width && image.Height == height ? image : image.SampleTo(width, height);
    var indexed = source.EnsureIndexedAtMost(ColorCount);

    // Three bits a channel is what the file states, so the palette is reduced before the pixels are
    // mapped onto it — otherwise two entries could collapse afterwards and take their pixels with them.
    var palette = new byte[ColorCount * 3];
    var stated = indexed.Palette ?? [];
    for (var i = 0; i < ColorCount * 3 && i < stated.Length; ++i)
      palette[i] = ChannelScaling.Expand3((stated[i] * 7 + 127) / 255);

    var stride = (width + 15) >> 4 << 3;
    var bitmap = AtariStGraphics.PackBitplanes(indexed.PixelData, stride, 4, width, height);
    var data = new byte[bitmap.Length];
    bitmap.CopyTo(data, 0);

    return new() {
      Data = data,
      Width = width,
      Height = height,
      BitmapOffset = 0,
      Bitplanes = 4,
      Palette = palette,
    };
  }
}
