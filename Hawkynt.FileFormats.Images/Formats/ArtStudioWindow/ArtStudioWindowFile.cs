using System;
using FileFormat.Core;

namespace FileFormat.ArtStudioWindow;

/// <summary>In-memory representation of an Art Studio window (.mwi, .mwin).</summary>
/// <remarks>
/// A rectangle cut out of a multicolour screen, stored cell by cell rather than as three separate
/// planes: each character carries its own video matrix byte, colour byte and eight bitmap rows in
/// one ten-byte record. Keeping a cell together is what makes the clipping meaningful — a window
/// out of a Koala-style picture would have had to carry three fragments with different strides.
/// <para/>
/// The cut need not fall on cell boundaries, so the header stores how far into the first cell the
/// picture starts. When it is not zero the window covers one more cell than its size implies, which
/// is what makes the stored length depend on the offset rather than only on the dimensions.
/// </remarks>
public readonly record struct ArtStudioWindowFile
  : IImageFormatReader<ArtStudioWindowFile>, IImageToRawImage<ArtStudioWindowFile>,
    IImageFromRawImage<ArtStudioWindowFile>, IImageFormatWriter<ArtStudioWindowFile> {

  /// <summary>Bytes a cell occupies: the two colour bytes and eight bitmap rows.</summary>
  public const int CellLength = 10;

  /// <summary>Offset of the first cell.</summary>
  public const int CellsOffset = 5;

  static string IImageFormatMetadata<ArtStudioWindowFile>.PrimaryExtension => ".mwi";
  static string[] IImageFormatMetadata<ArtStudioWindowFile>.FileExtensions => [".mwi", ".mwin"];
  static ArtStudioWindowFile IImageFormatReader<ArtStudioWindowFile>.FromSpan(ReadOnlySpan<byte> data)
    => ArtStudioWindowReader.FromSpan(data);
  static byte[] IImageFormatWriter<ArtStudioWindowFile>.ToBytes(ArtStudioWindowFile file)
    => ArtStudioWindowWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ArtStudioWindowFile>.VideoModes => [
    new("Window", [(new IntegerRange(2, 320), new IntegerRange(1, 200))], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Screen pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Cells across, including the partial one the cut may start in.</summary>
  public int CellsPerRow { get; init; }

  /// <summary>How far into the first cell the picture starts, across.</summary>
  public int Left { get; init; }

  /// <summary>How far into the first cell the picture starts, down.</summary>
  public int Top { get; init; }

  public static RawImage ToRawImage(ArtStudioWindowFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[file.Width * file.Height];

    for (var y = 0; y < file.Height; ++y)
    for (var x = 0; x < file.Width; ++x) {
      int screenY = file.Top + y, screenX = file.Left + x;
      var cell = CellsOffset + ((screenY >> 3) * file.CellsPerRow + (screenX >> 3)) * CellLength;
      var row = cell + 2 + (screenY & 7);
      var pattern = row < data.Length ? (data[row] >> (~screenX & 6)) & 3 : 0;

      pixels[y * file.Width + x] = (byte)(pattern switch {
        1 => (_At(data, cell) >> 4) & 15,
        2 => _At(data, cell) & 15,
        3 => _At(data, cell + 1) & 15,
        _ => 0,
      });
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Builds a window, choosing three colours for every cell beside the fixed background.</summary>
  /// <remarks>
  /// A cell is kept whole here — its two colour bytes and its eight bitmap rows in one ten-byte
  /// record — which is what makes a clipping meaningful: taken out of a Koala-style picture it
  /// would have had to carry three fragments with different strides.
  /// <para/>
  /// The cut is written aligned to cell boundaries. The format allows it to fall inside a cell, and
  /// stores how far in, but a window written from a whole picture has no reason to start off-grid
  /// and aligning it keeps the stored length a function of the size alone.
  /// </remarks>
  public static ArtStudioWindowFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The stored width counts multicolour pixels, each drawn two screen pixels wide, and it has to
    // fit a byte.
    var logical = Math.Clamp(image.Width / 2, 4, 255);
    logical -= logical % 4;
    var width = logical * 2;
    var height = Math.Clamp(image.Height, 1, 255);

    var columns = (width + 7) >> 3;
    var rows = (height + 7) >> 3;
    var rgb = image.SampleTo(width, height);

    var data = new byte[CellsOffset + rows * columns * CellLength];
    data[3] = (byte)logical;
    data[4] = (byte)height;

    Span<int> cell = stackalloc int[32];
    Span<int> chosen = stackalloc int[3];

    for (var cellRow = 0; cellRow < rows; ++cellRow)
    for (var column = 0; column < columns; ++column) {
      for (var y = 0; y < 8; ++y)
      for (var pixel = 0; pixel < 4; ++pixel) {
        var sourceY = Math.Min(cellRow * 8 + y, height - 1);
        var sourceX = Math.Min(column * 8 + pixel * 2, width - 1);
        var at = (sourceY * width + sourceX) * 3;
        cell[y * 4 + pixel] = Commodore64Graphics.FindNearestColorIndex(rgb.PixelData[at], rgb.PixelData[at + 1], rgb.PixelData[at + 2]);
      }

      _ChooseTriple(cell, chosen);

      var record = CellsOffset + (cellRow * columns + column) * CellLength;
      data[record] = (byte)((chosen[0] << 4) | chosen[1]);
      data[record + 1] = (byte)chosen[2];

      for (var y = 0; y < 8; ++y) {
        var value = 0;
        for (var pixel = 0; pixel < 4; ++pixel)
          value |= _Pattern(cell[y * 4 + pixel], chosen) << (6 - pixel * 2);

        data[record + 2 + y] = (byte)value;
      }
    }

    return new() { Data = data, Width = width, Height = height, CellsPerRow = columns, Left = 0, Top = 0 };
  }

  /// <summary>The three colours that, beside the fixed background, describe a cell best.</summary>
  private static void _ChooseTriple(ReadOnlySpan<int> cell, Span<int> chosen) {
    chosen[0] = chosen[1] = chosen[2] = 0;
    var bestError = long.MaxValue;

    for (var a = 0; a < Commodore64Graphics.ColorCount; ++a)
    for (var b = a; b < Commodore64Graphics.ColorCount; ++b)
    for (var c = b; c < Commodore64Graphics.ColorCount; ++c) {
      long error = 0;
      foreach (var index in cell)
        error += Math.Min(
          _Distance(index, 0),
          Math.Min(_Distance(index, a), Math.Min(_Distance(index, b), _Distance(index, c))));

      if (error >= bestError)
        continue;

      bestError = error;
      chosen[0] = a;
      chosen[1] = b;
      chosen[2] = c;
    }
  }

  /// <summary>Which of a cell's four colours describes a pixel best; pattern 0 is the background.</summary>
  private static int _Pattern(int index, ReadOnlySpan<int> chosen) {
    var pattern = 0;
    var best = _Distance(index, 0);

    for (var i = 0; i < 3; ++i) {
      var distance = _Distance(index, chosen[i]);
      if (distance >= best)
        continue;

      best = distance;
      pattern = i + 1;
    }

    return pattern;
  }

  /// <summary>Squared distance in RGB between two of the machine's colours, eye-weighted.</summary>
  private static long _Distance(int left, int right) {
    if (left == right)
      return 0;

    int a = Commodore64Graphics.HexColors[left], b = Commodore64Graphics.HexColors[right];
    long dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
    long dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
    long db = (a & 0xFF) - (b & 0xFF);

    return dr * dr * 77 + dg * dg * 150 + db * db * 29;
  }
}
