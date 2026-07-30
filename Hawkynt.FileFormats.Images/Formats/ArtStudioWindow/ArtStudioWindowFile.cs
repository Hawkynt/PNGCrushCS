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
  : IImageFormatReader<ArtStudioWindowFile>, IImageToRawImage<ArtStudioWindowFile> {

  /// <summary>Bytes a cell occupies: the two colour bytes and eight bitmap rows.</summary>
  public const int CellLength = 10;

  /// <summary>Offset of the first cell.</summary>
  public const int CellsOffset = 5;

  static string IImageFormatMetadata<ArtStudioWindowFile>.PrimaryExtension => ".mwi";
  static string[] IImageFormatMetadata<ArtStudioWindowFile>.FileExtensions => [".mwi", ".mwin"];
  static ArtStudioWindowFile IImageFormatReader<ArtStudioWindowFile>.FromSpan(ReadOnlySpan<byte> data)
    => ArtStudioWindowReader.FromSpan(data);
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
}
