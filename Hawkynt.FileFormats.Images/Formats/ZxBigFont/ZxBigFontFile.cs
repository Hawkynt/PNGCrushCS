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
  : IImageFormatReader<ZxBigFontFile>, IImageToRawImage<ZxBigFontFile>,
    IImageFromRawImage<ZxBigFontFile>, IImageFormatWriter<ZxBigFontFile> {

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
  static byte[] IImageFormatWriter<ZxBigFontFile>.ToBytes(ZxBigFontFile file)
    => ZxBigFontWriter.ToBytes(file);
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

  /// <summary>Bytes an opaque cell occupies: eight of bitmap and one of attribute.</summary>
  public const int OpaqueCellLength = 9;

  /// <summary>Builds a font where every character is one cell, which is the sheet at its smallest.</summary>
  /// <remarks>
  /// Characters here are variable size and reached through an offset table, so a writer has to
  /// decide their shape rather than discover it. Giving all 256 a single opaque cell makes the
  /// sheet exactly sixteen by sixteen cells and every character the same eight by eight — which is
  /// what a picture of a character set is, and what the reader then measures back out of the table.
  /// <para/>
  /// Opaque rather than transparent: a transparent character stores no attribute and so cannot
  /// carry its own colours, and a sheet drawn in the default pair everywhere would lose whatever
  /// colour the picture had.
  /// </remarks>
  public static ZxBigFontFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    const int columns = 1, rows = 1;
    var width = SheetColumns * 8;
    var height = CharacterCount / SheetColumns * 8;
    var rgb = image.SampleTo(width, height);

    var tableEnd = OffsetTableStart + CharacterCount * 2;
    var record = 3 + columns * rows * OpaqueCellLength;
    var data = new byte[tableEnd + CharacterCount * record];

    data[0] = (byte)'C';
    data[1] = (byte)'H';
    data[2] = (byte)'X';

    Span<byte> bits = stackalloc byte[8];

    for (var character = 0; character < CharacterCount; ++character) {
      var offset = tableEnd + character * record;
      data[OffsetTableStart + character * 2] = (byte)offset;
      data[OffsetTableStart + 1 + character * 2] = (byte)(offset >> 8);

      data[offset] = 0;
      data[offset + 1] = columns;
      data[offset + 2] = rows;

      var left = character % SheetColumns * 8;
      var top = character / SheetColumns * 8;

      data[offset + 3 + 8] = ZxSpectrumGraphics.ChooseCell(rgb.PixelData, width, left, top, bits);
      for (var y = 0; y < 8; ++y)
        data[offset + 3 + y] = bits[y];
    }

    return new() { Data = data, MaxColumns = columns, MaxRows = rows };
  }
}
