using System;
using FileFormat.Core;

namespace FileFormat.StarPainter;

/// <summary>In-memory representation of a Star Painter picture (.gr, .cs) for the Commodore 64.</summary>
/// <remarks>
/// Two bytes of size and then a high-resolution bitmap in the C64's cell layout. The size is given
/// in character cells rather than pixels — a column count and a row count — so a picture is always
/// a whole number of cells, which is what the bitmap layout requires anyway.
/// <para/>
/// No colours are stored at all. The picture is black on white, and where an ordinary C64 hires
/// screen would consult its video matrix this one has a constant standing in for it.
/// </remarks>
public readonly record struct StarPainterFile
  : IImageFormatReader<StarPainterFile>, IImageToRawImage<StarPainterFile>,
    IImageFromRawImage<StarPainterFile>, IImageFormatWriter<StarPainterFile> {

  /// <summary>Size of the header: columns then rows, both in character cells.</summary>
  public const int HeaderSize = 2;

  /// <summary>Pixels a character cell spans in each direction.</summary>
  public const int CellSize = 8;

  /// <summary>Palette entry a clear bit draws.</summary>
  public const int PaperIndex = 1;

  /// <summary>Palette entry a set bit draws.</summary>
  public const int InkIndex = 0;

  static string IImageFormatMetadata<StarPainterFile>.PrimaryExtension => ".gr";
  static string[] IImageFormatMetadata<StarPainterFile>.FileExtensions => [".gr", ".cs"];
  static StarPainterFile IImageFormatReader<StarPainterFile>.FromSpan(ReadOnlySpan<byte> data)
    => StarPainterReader.FromSpan(data);
  static byte[] IImageFormatWriter<StarPainterFile>.ToBytes(StarPainterFile file)
    => StarPainterWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<StarPainterFile>.VideoModes => [
    new("Star Painter", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];

  /// <summary>Character cells across.</summary>
  public int Columns { get; init; }

  /// <summary>Character cells down.</summary>
  public int Rows { get; init; }

  /// <summary>The bitmap, one bit per pixel, in the C64's cell layout.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>Picture width in pixels.</summary>
  public int Width => this.Columns * CellSize;

  /// <summary>Picture height in pixels.</summary>
  public int Height => this.Rows * CellSize;

  public static RawImage ToRawImage(StarPainterFile file) {
    var data = file.BitmapData ?? [];
    var width = file.Width;
    var height = file.Height;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      // A cell's eight rows are consecutive bytes, and cells run left to right then down.
      var offset = (y & ~7) * file.Columns + (x & ~7) + (y & 7);
      var set = offset < data.Length && ((data[offset] >> (~x & 7)) & 1) != 0;
      pixels[y * width + x] = (byte)(set ? InkIndex : PaperIndex);
    }

    var palette = Commodore64Graphics.CreatePalette();

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  public static StarPainterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width % CellSize != 0 || image.Height % CellSize != 0)
      throw new ArgumentException(
        $"A Star Painter picture is a whole number of {CellSize}-pixel cells, got {image.Width}x{image.Height}.", nameof(image));

    var columns = image.Width / CellSize;
    var rows = image.Height / CellSize;
    if (columns is < 1 or > 255 || rows is < 1 or > 255)
      throw new ArgumentException($"A Star Painter picture is at most 255 cells each way, got {columns}x{rows}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var palette = Commodore64Graphics.CreatePalette();
    var data = new byte[columns * rows * CellSize];

    for (var y = 0; y < image.Height; ++y)
    for (var x = 0; x < image.Width; ++x) {
      var pixel = (y * image.Width + x) * 4;
      // Only two colours exist and neither is stored, so a pixel is ink when it is nearer black.
      if (_Distance(palette, InkIndex, bgra.PixelData, pixel) < _Distance(palette, PaperIndex, bgra.PixelData, pixel))
        data[(y & ~7) * columns + (x & ~7) + (y & 7)] |= (byte)(0x80 >> (x & 7));
    }

    return new() { Columns = columns, Rows = rows, BitmapData = data };
  }

  private static int _Distance(ReadOnlySpan<byte> palette, int entry, ReadOnlySpan<byte> bgra, int pixel) {
    int dr = palette[entry * 3] - bgra[pixel + 2];
    int dg = palette[entry * 3 + 1] - bgra[pixel + 1];
    int db = palette[entry * 3 + 2] - bgra[pixel];

    return dr * dr + dg * dg + db * db;
  }
}
