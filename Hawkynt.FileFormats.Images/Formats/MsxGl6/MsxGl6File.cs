using System;
using FileFormat.Core;

namespace FileFormat.MsxGl6;

/// <summary>In-memory representation of an MSX2 GL6 picture or Dynamic Publisher stamp
/// (.gl6, .sh6, .stp).</summary>
/// <remarks>
/// A four-byte header giving the dimensions, then a Screen 6 bitmap at two bits per pixel. Screen 6
/// puts 512 pixels on a line by halving the vertical resolution, so every stored row is drawn on
/// two scanlines and a picture is twice as tall as it is stored. The four colours come from a
/// companion <c>.PL6</c> palette; a stamp has none and is simply black on white.
/// </remarks>
public readonly record struct MsxGl6File
  : IImageFormatReader<MsxGl6File>, IImageToRawImage<MsxGl6File>,
    IImageFromRawImage<MsxGl6File>, IImageFormatWriter<MsxGl6File> {

  /// <summary>Size of the header: width then height, each a little-endian 16-bit value.</summary>
  public const int HeaderSize = 4;

  /// <summary>Colours a Screen 6 picture can show at once.</summary>
  public const int ColorCount = 4;

  /// <summary>Pixels one byte holds.</summary>
  public const int PixelsPerByte = 4;

  /// <summary>Largest picture we accept, guarding against a corrupt header claiming gigabytes.</summary>
  public const int MaxDimension = 4096;

  static string IImageFormatMetadata<MsxGl6File>.PrimaryExtension => ".gl6";
  static string[] IImageFormatMetadata<MsxGl6File>.FileExtensions => [".gl6", ".sh6", ".stp"];
  static MsxGl6File IImageFormatReader<MsxGl6File>.FromSpan(ReadOnlySpan<byte> data) => MsxGl6Reader.FromSpan(data);
  static byte[] IImageFormatWriter<MsxGl6File>.ToBytes(MsxGl6File file) => MsxGl6Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<MsxGl6File>.VideoModes => [
    new("Screen 6", [(512, 424)], [ColorCount])
  ];

  /// <summary>Bytes a bitmap of the given size occupies.</summary>
  public static int PixelDataSizeFor(int width, int height) => (width * height + PixelsPerByte - 1) / PixelsPerByte;

  /// <summary>Stored width.</summary>
  public int Width { get; init; }

  /// <summary>Stored height; the picture is drawn twice as tall.</summary>
  public int Height { get; init; }

  /// <summary>The bitmap, four pixels per byte, most significant pair leftmost.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>
  /// The four colours, two bytes each, or empty for the machine's default of black on white. The
  /// palette lives in a companion <c>.PL6</c> file rather than in this one.
  /// </summary>
  public byte[] Palette { get; init; }

  /// <summary>Which of the two this file is.</summary>
  public MsxGl6Kind Kind { get; init; }

  /// <summary>Which kind an extension names.</summary>
  public static MsxGl6Kind KindFromExtension(string extension)
    => extension.ToLowerInvariant() == ".stp" ? MsxGl6Kind.Stamp : MsxGl6Kind.Picture;

  /// <summary>
  /// The colours a file draws with when no companion palette is beside it.
  /// </summary>
  /// <remarks>
  /// A stamp never has a companion and is simply black on white paper. A picture expects one, and
  /// when it is missing the machine is still showing the four colours Screen 6 starts up with —
  /// black and three greens — so that, not white paper, is what the picture means.
  /// </remarks>
  private static byte[] _DefaultPaletteRgb(MsxGl6Kind kind) => kind == MsxGl6Kind.Stamp
    ? [255, 255, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0]
    : MsxGraphics.Screen6DefaultPaletteRgb.ToArray();

  public static RawImage ToRawImage(MsxGl6File file) {
    var data = file.PixelData ?? [];
    var stored = file.Palette ?? [];
    var width = file.Width;
    var height = file.Height * 2;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var offset = (y >> 1) * width + x;
      var index = offset / PixelsPerByte;
      var b = index < data.Length ? data[index] : 0;
      pixels[y * width + x] = (byte)((b >> ((~offset & 3) << 1)) & 3);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = stored.Length > 0 ? MsxGraphics.PaletteToRgb(stored, ColorCount) : _DefaultPaletteRgb(file.Kind),
      PaletteCount = ColorCount,
    };
  }

  public static MsxGl6File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 2 || image.Width > MaxDimension || image.Height > MaxDimension)
      throw new ArgumentException($"A GL6 picture is at most {MaxDimension}x{MaxDimension}, got {image.Width}x{image.Height}.", nameof(image));

    // Two scanlines per stored row, so an odd height would leave a row half-described.
    if ((image.Height & 1) != 0)
      throw new ArgumentException($"A GL6 picture has an even height; got {image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var quantized = ColorQuantizer.Quantize(bgra.PixelData, image.Width * image.Height, ColorCount);
    var stored = image.Height / 2;
    var data = new byte[PixelDataSizeFor(image.Width, stored)];

    // Only the first of each pair of scanlines is kept; the machine shows it twice regardless.
    for (var y = 0; y < stored; ++y)
    for (var x = 0; x < image.Width; ++x) {
      var offset = y * image.Width + x;
      var index = quantized.Indices[y * 2 * image.Width + x] & 3;
      data[offset / PixelsPerByte] |= (byte)(index << ((~offset & 3) << 1));
    }

    return new() {
      Width = image.Width,
      Height = stored,
      PixelData = data,
      Palette = MsxGraphics.PaletteFromRgb(quantized.Palette, quantized.Count, ColorCount),
    };
  }
}
