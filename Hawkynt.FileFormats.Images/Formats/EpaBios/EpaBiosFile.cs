using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.EpaBios;

/// <summary>In-memory representation of an Award BIOS Logo (.epa) image.</summary>
/// <remarks>
/// A BIOS logo is not a bitmap: it is a screenful of text-mode character cells, each an eight by
/// fourteen glyph plus the attribute byte naming the two colours it is drawn in. That is why the
/// file is so small for its size, and why it can only hold two colours in any eight-by-fourteen
/// square.
/// <para/>
/// It used to be read here as 714 bytes of flat eight-bit indices — a picture of the right shape,
/// the wrong length, and no relation to what a BIOS stores.
/// </remarks>
public readonly record struct EpaBiosFile : IImageFormatReader<EpaBiosFile>, IImageToRawImage<EpaBiosFile>, IImageFromRawImage<EpaBiosFile>, IImageFormatWriter<EpaBiosFile> {

  /// <summary>Pixels across one character cell.</summary>
  internal const int CellWidth = 8;

  /// <summary>Scanlines down one character cell.</summary>
  internal const int CellHeight = 14;

  /// <summary>Bytes the file carries past the picture, which no reader looks at.</summary>
  internal const int TrailerSize = 70;

  /// <summary>The most cells a BIOS screen has, being a text screen.</summary>
  internal const int MaxColumns = 80;

  internal const int MaxRows = 25;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFormatMetadata<EpaBiosFile>.PrimaryExtension => ".epa";
  static string[] IImageFormatMetadata<EpaBiosFile>.FileExtensions => [".epa"];
  static EpaBiosFile IImageFormatReader<EpaBiosFile>.FromSpan(ReadOnlySpan<byte> data) => EpaBiosReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<EpaBiosFile>.VideoModes => [
    new("Default", [(136, 84)], [16])
  ];
  static byte[] IImageFormatWriter<EpaBiosFile>.ToBytes(EpaBiosFile file) => EpaBiosWriter.ToBytes(file);

  /// <summary>Character cells across.</summary>
  public int Columns { get; init; }

  /// <summary>Character cells down.</summary>
  public int Rows { get; init; }

  /// <summary>One attribute byte a cell: the background in the high nibble, the ink in the low one.</summary>
  public byte[] Attributes { get; init; }

  /// <summary>Fourteen bytes a cell, one per scanline, the leftmost pixel in the top bit.</summary>
  public byte[] Glyphs { get; init; }

  public int Width => this.Columns * CellWidth;
  public int Height => this.Rows * CellHeight;

  /// <summary>The length a file with the given cell counts has.</summary>
  internal static int SizeOf(int columns, int rows) => 2 + columns * rows * (1 + CellHeight) + TrailerSize;

  public static RawImage ToRawImage(EpaBiosFile file) {
    var width = file.Width;
    var height = file.Height;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = y / CellHeight * file.Columns + x / CellWidth;
      var attribute = file.Attributes[cell];
      var lit = (file.Glyphs[cell * CellHeight + y % CellHeight] >> (~x & 7)) & 1;
      pixels[y * width + x] = (byte)(lit == 0 ? attribute >> 4 : attribute & 15);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _DefaultPalette[..],
      PaletteCount = 16,
    };
  }

  /// <summary>Fits a picture into character cells of two colours each.</summary>
  /// <remarks>
  /// Each cell gets the two palette entries that appear most often in it, the more common one as
  /// the background; every pixel then goes to whichever of the two it is nearer. A cell of one
  /// colour therefore stays one colour, and a cell of many loses all but two — which is the whole
  /// of what the format can say.
  /// </remarks>
  public static EpaBiosFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var columns = Math.Clamp((image.Width + CellWidth / 2) / CellWidth, 1, MaxColumns);
    var rows = Math.Clamp((image.Height + CellHeight / 2) / CellHeight, 1, MaxRows);
    var width = columns * CellWidth;
    var height = rows * CellHeight;

    if (image.Width != width || image.Height != height)
      image = image.SampleTo(width, height);

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, _DefaultPalette);
    var attributes = new byte[columns * rows];
    var glyphs = new byte[columns * rows * CellHeight];

    Span<int> frequency = stackalloc int[16];
    for (var row = 0; row < rows; ++row)
    for (var column = 0; column < columns; ++column) {
      frequency.Clear();
      for (var y = 0; y < CellHeight; ++y)
      for (var x = 0; x < CellWidth; ++x)
        ++frequency[indexed.PixelData[(row * CellHeight + y) * width + column * CellWidth + x] & 15];

      int background = 0, ink = 0;
      for (var i = 0; i < 16; ++i) {
        if (frequency[i] > frequency[background]) {
          ink = background;
          background = i;
        } else if (i != background && frequency[i] > frequency[ink])
          ink = i;
      }

      var cell = row * columns + column;
      attributes[cell] = (byte)((background << 4) | ink);

      for (var y = 0; y < CellHeight; ++y) {
        byte bits = 0;
        for (var x = 0; x < CellWidth; ++x) {
          var index = indexed.PixelData[(row * CellHeight + y) * width + column * CellWidth + x] & 15;
          if (_Nearer(index, ink, background))
            bits |= (byte)(1 << (7 - x));
        }

        glyphs[cell * CellHeight + y] = bits;
      }
    }

    return new() {
      Columns = columns,
      Rows = rows,
      Attributes = attributes,
      Glyphs = glyphs,
    };
  }

  /// <summary>Whether a palette entry is closer to the ink than to the background.</summary>
  private static bool _Nearer(int index, int ink, int background) {
    if (index == ink)
      return true;
    if (index == background)
      return false;

    return _Distance(index, ink) < _Distance(index, background);
  }

  private static int _Distance(int a, int b) {
    int dr = _DefaultPalette[a * 3] - _DefaultPalette[b * 3];
    int dg = _DefaultPalette[a * 3 + 1] - _DefaultPalette[b * 3 + 1];
    int db = _DefaultPalette[a * 3 + 2] - _DefaultPalette[b * 3 + 2];
    return dr * dr + dg * dg + db * db;
  }
}
