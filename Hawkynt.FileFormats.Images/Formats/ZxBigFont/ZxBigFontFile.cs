using System;
using FileFormat.Core;

namespace FileFormat.ZxBigFont;

/// <summary>In-memory representation of a ZX Spectrum big font (.chx).</summary>
/// <remarks>
/// A font whose characters are not one cell but a rectangle of them, so a letter can be as large as
/// the designer wants. Each character names its own size and is stored at an offset the header
/// points to, and a character may be absent altogether; the sheet is laid out sixteen characters
/// across at the size of the largest, which is why the whole table has to be walked before anything
/// can be drawn.
/// </remarks>
public readonly record struct ZxBigFontFile
  : IImageFormatReader<ZxBigFontFile>, IImageToRawImage<ZxBigFontFile> {

  /// <summary>Characters a font holds, present or not.</summary>
  public const int CharacterCount = 256;

  /// <summary>Characters laid out across the sheet.</summary>
  public const int SheetColumns = 16;

  /// <summary>Bytes before the offset table: the signature and two bytes of version.</summary>
  public const int OffsetTableStart = 5;

  static string IImageFormatMetadata<ZxBigFontFile>.PrimaryExtension => ".chx";
  static string[] IImageFormatMetadata<ZxBigFontFile>.FileExtensions => [".chx"];
  static ZxBigFontFile IImageFormatReader<ZxBigFontFile>.FromSpan(ReadOnlySpan<byte> data)
    => ZxBigFontReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<ZxBigFontFile>.VideoModes => [
    new("ZX Spectrum", [(IntegerRange.Any, IntegerRange.Any)], [15])
  ];

  /// <summary>The whole file, which the offset table points into.</summary>
  public byte[] Data { get; init; }

  /// <summary>Cells across in the widest character.</summary>
  public int MaxColumns { get; init; }

  /// <summary>Cells down in the tallest character.</summary>
  public int MaxRows { get; init; }

  /// <summary>Where a character's description starts, or zero if it has none.</summary>
  public static int TileOffset(ReadOnlySpan<byte> data, int character)
    => data[OffsetTableStart + character * 2] | (data[OffsetTableStart + 1 + character * 2] << 8);

  public static RawImage ToRawImage(ZxBigFontFile file) {
    var data = file.Data ?? [];
    var width = file.MaxColumns * 8 * SheetColumns;
    var height = file.MaxRows * 8 * (CharacterCount / SheetColumns);
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var sheetRow = y >> 3;
      var characterRow = sheetRow / file.MaxRows << 4;
      var row = sheetRow % file.MaxRows;

      for (var x = 0; x < width; ++x) {
        var sheetColumn = x >> 3;
        var character = characterRow + sheetColumn / file.MaxColumns;
        var column = sheetColumn % file.MaxColumns;

        // Where a character is absent, or does not reach this far, the sheet shows a chequer of
        // paper and ink in the default colours rather than a hole.
        var bits = ~x ^ y;
        var attribute = (byte)56;

        var offset = TileOffset(data, character);
        if (offset > 0) {
          int columns = data[offset + 1];
          if (column < columns && row < data[offset + 2]) {
            var transparent = data[offset];
            offset += 3 + (row * columns + column) * (9 - transparent);
            bits = data[offset + (y & 7)] >> (~x & 7);

            // A transparent character carries no attributes, so it keeps the default colours.
            if (transparent == 0)
              attribute = data[offset + 8];
          }
        }

        ZxSpectrumGraphics.WriteRgb(rgb, (y * width + x) * 3, attribute, (bits & 1) != 0);
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }
}
