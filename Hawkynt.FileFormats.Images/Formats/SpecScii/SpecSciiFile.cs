using System;
using FileFormat.Core;

namespace FileFormat.SpecScii;

/// <summary>In-memory representation of a SpecSCII picture (.zxs).</summary>
/// <remarks>
/// A Spectrum screen drawn out of a character set rather than as a bitmap: 112 characters, and then
/// one index and one attribute per cell. That is what makes the file 2452 bytes where a screen is
/// 6912 — a picture built from repeated shapes costs a byte a cell instead of eight.
/// <para/>
/// The two cell maps are stored column by column rather than row by row, so consecutive bytes run
/// down the screen and not across it.
/// </remarks>
public readonly record struct SpecSciiFile
  : IImageFormatReader<SpecSciiFile>, IImageToRawImage<SpecSciiFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = ZxSpectrumGraphics.ScreenWidth;

  /// <summary>Rows.</summary>
  public const int Height = ZxSpectrumGraphics.ScreenHeight;

  /// <summary>Cells across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Cell rows.</summary>
  public const int Rows = Height / 8;

  /// <summary>Characters the set holds.</summary>
  public const int CharacterCount = 112;

  /// <summary>Offset of the character set.</summary>
  public const int CharactersOffset = 12;

  /// <summary>Offset of the cell indices.</summary>
  public const int ScreenOffset = CharactersOffset + CharacterCount * 8;

  /// <summary>Offset of the cell attributes.</summary>
  public const int AttributeOffset = ScreenOffset + Columns * Rows;

  /// <summary>Total file size.</summary>
  public const int FileSize = AttributeOffset + Columns * Rows + 8;

  /// <summary>The string every file starts with.</summary>
  public const string Signature = "ZX_SSCII";

  static string IImageFormatMetadata<SpecSciiFile>.PrimaryExtension => ".zxs";
  static string[] IImageFormatMetadata<SpecSciiFile>.FileExtensions => [".zxs"];
  static SpecSciiFile IImageFormatReader<SpecSciiFile>.FromSpan(ReadOnlySpan<byte> data)
    => SpecSciiReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SpecSciiFile>.VideoModes => [
    new("SpecSCII", [(Width, Height)], [ZxSpectrumGraphics.PaletteEntryCount])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(SpecSciiFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      // Column-major: a cell's index is its column times the row count plus its row.
      var cell = (x >> 3) * Rows + (y >> 3);
      var character = data[ScreenOffset + cell];
      var attribute = data[AttributeOffset + cell];

      var at = CharactersOffset + character * 8 + (y & 7);
      var ink = at < data.Length && ((data[at] >> (~x & 7)) & 1) != 0;
      pixels[y * Width + x] = (byte)ZxSpectrumGraphics.ColorIndex(attribute, ink);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = ZxSpectrumGraphics.Palette.ToArray(),
      PaletteCount = ZxSpectrumGraphics.PaletteEntryCount,
    };
  }
}
