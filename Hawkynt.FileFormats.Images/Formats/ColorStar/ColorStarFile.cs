using System;
using FileFormat.Core;

namespace FileFormat.ColorStar;

/// <summary>In-memory representation of a ColorSTar picture (.bil) for the Atari ST.</summary>
/// <remarks>
/// A sixteen-colour palette and then a low-resolution screen, with no header beyond the palette
/// itself. Some files carry two leading zero bytes before it — a length field the format never
/// grew into — which is the only thing distinguishing the two sizes it comes in.
/// </remarks>
public readonly record struct ColorStarFile
  : IImageFormatReader<ColorStarFile>, IImageToRawImage<ColorStarFile>,
    IImageFromRawImage<ColorStarFile>, IImageFormatWriter<ColorStarFile> {

  /// <summary>Picture width.</summary>
  public const int Width = 320;

  /// <summary>Picture height.</summary>
  public const int Height = 200;

  /// <summary>Bitplanes a low-resolution screen uses.</summary>
  public const int Planes = 4;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 1 << Planes;

  /// <summary>Size of the stored palette.</summary>
  public const int PaletteSize = ColorCount * AtariStGraphics.PaletteEntrySize;

  /// <summary>Size of the bitmap.</summary>
  public static readonly int BitmapSize = AtariStGraphics.BytesPerRow(Width, Planes) * Height;

  /// <summary>Size of a file that starts with its palette.</summary>
  public static readonly int PlainFileSize = PaletteSize + BitmapSize;

  /// <summary>Size of a file with the two leading bytes some writers add.</summary>
  public static readonly int PrefixedFileSize = PlainFileSize + 2;

  static string IImageFormatMetadata<ColorStarFile>.PrimaryExtension => ".bil";
  static string[] IImageFormatMetadata<ColorStarFile>.FileExtensions => [".bil"];
  static ColorStarFile IImageFormatReader<ColorStarFile>.FromSpan(ReadOnlySpan<byte> data) => ColorStarReader.FromSpan(data);
  static byte[] IImageFormatWriter<ColorStarFile>.ToBytes(ColorStarFile file) => ColorStarWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ColorStarFile>.VideoModes => [
    new("ColorSTar", [(Width, Height)], [ColorCount])
  ];

  /// <summary>The palette as stored, two bytes an entry.</summary>
  public byte[] Palette { get; init; }

  /// <summary>The bitmap, four planes interleaved by word.</summary>
  public byte[] BitmapData { get; init; }

  public static RawImage ToRawImage(ColorStarFile file) => new() {
    Width = Width,
    Height = Height,
    Format = PixelFormat.Indexed8,
    PixelData = PlanarConverter.AtariStToChunky(file.BitmapData ?? [], Width, Height, Planes),
    Palette = AtariStGraphics.ReadPalette(file.Palette ?? [], 0, ColorCount),
    PaletteCount = ColorCount,
  };

  public static ColorStarFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException($"Expected {Width}x{Height} but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = PixelConverter.Convert(image, PixelFormat.Indexed8);
    var rgb = indexed.Palette ?? [];

    // Three bits a channel: the plain ST form, which every machine reads.
    var palette = new byte[PaletteSize];
    for (var i = 0; i < ColorCount && i < indexed.PaletteCount; ++i) {
      int r = (rgb[i * 3] * 7 + 127) / 255, g = (rgb[i * 3 + 1] * 7 + 127) / 255, b = (rgb[i * 3 + 2] * 7 + 127) / 255;
      palette[i * 2] = (byte)r;
      palette[i * 2 + 1] = (byte)((g << 4) | b);
    }

    return new() {
      Palette = palette,
      BitmapData = PlanarConverter.ChunkyToAtariSt(indexed.PixelData, Width, Height, Planes),
    };
  }
}
