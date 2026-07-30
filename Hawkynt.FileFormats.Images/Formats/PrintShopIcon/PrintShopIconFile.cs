using System;
using FileFormat.Core;

namespace FileFormat.PrintShopIcon;

/// <summary>In-memory representation of a Print Shop graphic (.psf) for the Atari 8-bit.</summary>
/// <remarks>
/// One of the clip-art pieces Print Shop stamped into cards and banners: 88 by 52 at one bit per
/// pixel, eleven bytes to a row, and nothing else in the file. The odd dimensions are the printer's
/// rather than the screen's — this was never meant to be displayed, only stamped.
/// <para/>
/// A set bit is ink. The two colours are the Atari's, not pure black and white: the paper is GTIA
/// colour 14, which is a light grey, because that is what the program drew its preview on.
/// </remarks>
public readonly record struct PrintShopIconFile
  : IImageFormatReader<PrintShopIconFile>, IImageToRawImage<PrintShopIconFile>,
    IImageFromRawImage<PrintShopIconFile>, IImageFormatWriter<PrintShopIconFile> {

  /// <summary>Graphic width.</summary>
  public const int Width = 88;

  /// <summary>Graphic height.</summary>
  public const int Height = 52;

  /// <summary>Bytes one row occupies.</summary>
  public const int BytesPerRow = Width / 8;

  /// <summary>Size of the bitmap.</summary>
  public const int BitmapSize = BytesPerRow * Height;

  /// <summary>Largest file we accept; some carry trailing bytes the graphic does not use.</summary>
  public const int MaxFileSize = 640;

  /// <summary>GTIA colour of the paper.</summary>
  public const byte PaperColor = 14;

  /// <summary>GTIA colour of the ink.</summary>
  public const byte InkColor = 0;

  static string IImageFormatMetadata<PrintShopIconFile>.PrimaryExtension => ".psf";
  static string[] IImageFormatMetadata<PrintShopIconFile>.FileExtensions => [".psf"];
  static PrintShopIconFile IImageFormatReader<PrintShopIconFile>.FromSpan(ReadOnlySpan<byte> data)
    => PrintShopIconReader.FromSpan(data);
  static byte[] IImageFormatWriter<PrintShopIconFile>.ToBytes(PrintShopIconFile file)
    => PrintShopIconWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<PrintShopIconFile>.VideoModes => [
    new("Print Shop graphic", [(Width, Height)], [2])
  ];

  /// <summary>The bitmap, one bit per pixel, most significant bit leftmost.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>The two colours as RGB triplets, paper first.</summary>
  internal static byte[] PaletteRgb() {
    var gtia = Atari8BitGraphics.Palette;
    var palette = new byte[6];
    gtia.Slice(PaperColor * 3, 3).CopyTo(palette);
    gtia.Slice(InkColor * 3, 3).CopyTo(palette.AsSpan(3));

    return palette;
  }

  public static RawImage ToRawImage(PrintShopIconFile file) {
    var data = file.BitmapData ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var index = y * BytesPerRow + (x >> 3);
      var b = index < data.Length ? data[index] : 0;
      pixels[y * Width + x] = (byte)((b >> (~x & 7)) & 1);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = PaletteRgb(),
      PaletteCount = 2,
    };
  }

  public static PrintShopIconFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != Width || image.Height != Height)
      throw new ArgumentException($"Expected {Width}x{Height} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var palette = PaletteRgb();
    var data = new byte[BitmapSize];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var pixel = (y * Width + x) * 4;
      // Two colours and neither stored, so a pixel is ink when it is nearer the ink than the paper.
      if (_Distance(palette, 1, bgra.PixelData, pixel) < _Distance(palette, 0, bgra.PixelData, pixel))
        data[y * BytesPerRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
    }

    return new() { BitmapData = data };
  }

  private static int _Distance(ReadOnlySpan<byte> palette, int entry, ReadOnlySpan<byte> bgra, int pixel) {
    int dr = palette[entry * 3] - bgra[pixel + 2];
    int dg = palette[entry * 3 + 1] - bgra[pixel + 1];
    int db = palette[entry * 3 + 2] - bgra[pixel];

    return dr * dr + dg * dg + db * db;
  }
}
