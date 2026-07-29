using System;
using FileFormat.Core;

namespace FileFormat.SevenuP;

/// <summary>In-memory representation of a ZX Spectrum SevenuP (.sev) image.</summary>
/// <remarks>
/// Unusually for a Spectrum format this one is self-describing: a header naming the dimensions,
/// then the picture stored cell by cell rather than as a screen dump. Each 8x8 cell occupies nine
/// consecutive bytes — its eight bitmap rows followed by its attribute — so the display file's
/// interleaved addressing does not apply and a cell's data is contiguous.
/// </remarks>
public readonly record struct SevenuPFile
  : IImageFormatReader<SevenuPFile>, IImageToRawImage<SevenuPFile>,
    IImageFromRawImage<SevenuPFile>, IImageFormatWriter<SevenuPFile> {

  /// <summary>ASCII tag every SevenuP file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => "Sev"u8;

  /// <summary>Offset of the little-endian width.</summary>
  public const int WidthOffset = 10;

  /// <summary>Offset of the little-endian height.</summary>
  public const int HeightOffset = 12;

  /// <summary>Offset of the cell data.</summary>
  public const int CellDataOffset = 14;

  /// <summary>Bytes per cell: eight bitmap rows plus one attribute.</summary>
  public const int BytesPerCell = 9;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Cell data, nine bytes per cell in reading order.</summary>
  public byte[] CellData { get; init; }

  static string IImageFormatMetadata<SevenuPFile>.PrimaryExtension => ".sev";
  static string[] IImageFormatMetadata<SevenuPFile>.FileExtensions => [".sev"];
  static SevenuPFile IImageFormatReader<SevenuPFile>.FromSpan(ReadOnlySpan<byte> data) => SevenuPReader.FromSpan(data);
  static byte[] IImageFormatWriter<SevenuPFile>.ToBytes(SevenuPFile file) => SevenuPWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SevenuPFile>.VideoModes => [
    new("SevenuP", [(new IntegerRange(8, 1024, 8), new IntegerRange(8, 1024, 8))],
        [ZxSpectrumGraphics.PaletteEntryCount])
  ];

  /// <summary>Cells needed to span the given pixel count.</summary>
  public static int CellsFor(int pixels) => (pixels + 7) >> 3;

  /// <summary>File size for the given dimensions.</summary>
  public static int FileSizeFor(int width, int height)
    => CellDataOffset + CellsFor(width) * CellsFor(height) * BytesPerCell;

  public static RawImage ToRawImage(SevenuPFile file) {
    int width = file.Width, height = file.Height;
    var columns = CellsFor(width);

    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = ((y >> 3) * columns + (x >> 3)) * BytesPerCell;
      var set = ((file.CellData[cell + (y & 7)] >> (~x & 7)) & 1) != 0;
      pixels[y * width + x] = (byte)ZxSpectrumGraphics.ColorIndex(file.CellData[cell + 8], set);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = ZxSpectrumGraphics.Palette.ToArray(),
      PaletteCount = ZxSpectrumGraphics.PaletteEntryCount,
    };
  }

  public static SevenuPFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width % 8 != 0 || image.Height % 8 != 0)
      throw new ArgumentException($"Dimensions must be whole 8x8 cells but got {image.Width}x{image.Height}.", nameof(image));

    var indexed = image.EnsureIndexed(PixelFormat.Indexed8, ZxSpectrumGraphics.Palette.ToArray());
    int width = image.Width, height = image.Height;
    int columns = CellsFor(width), rows = CellsFor(height);
    var cells = new byte[columns * rows * BytesPerCell];

    // A cell holds two colours, so each one keeps the two that appear most within it.
    Span<int> counts = stackalloc int[ZxSpectrumGraphics.PaletteEntryCount];
    for (var cellY = 0; cellY < rows; ++cellY)
    for (var cellX = 0; cellX < columns; ++cellX) {
      counts.Clear();
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        ++counts[indexed.PixelData[(cellY * 8 + y) * width + cellX * 8 + x] & 15];

      int paper = 0, ink = 0;
      for (var c = 1; c < counts.Length; ++c)
        if (counts[c] > counts[paper])
          paper = c;
      for (var c = 0; c < counts.Length; ++c)
        if (c != paper && counts[c] > counts[ink == paper ? paper : ink])
          ink = c;

      var cell = (cellY * columns + cellX) * BytesPerCell;
      for (var y = 0; y < 8; ++y) {
        var bits = 0;
        for (var x = 0; x < 8; ++x)
          if ((indexed.PixelData[(cellY * 8 + y) * width + cellX * 8 + x] & 15) == ink)
            bits |= 0x80 >> x;

        cells[cell + y] = (byte)bits;
      }

      cells[cell + 8] = ZxSpectrumGraphics.Attribute(ink, paper);
    }

    return new() { Width = width, Height = height, CellData = cells };
  }
}
