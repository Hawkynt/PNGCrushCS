using System;
using FileFormat.Core;

namespace FileFormat.StarPainterFont;

/// <summary>In-memory representation of a Star Painter character set (.zs).</summary>
/// <remarks>
/// A hundred and thirteen characters rather than the 128 or 256 a character set usually holds, and
/// nine bytes each rather than eight. The ninth byte is not part of the shape — it is what the
/// editor kept beside each character — so the set is laid out as records rather than as a
/// contiguous bitmap, and a reader that steps eight bytes at a time drifts one byte further out of
/// alignment with every character.
/// <para/>
/// Shown as four rows of thirty-two, which is what fills the 256-pixel width the C64 draws in; the
/// fifteen cells past the end of the set stay blank.
/// </remarks>
public readonly record struct StarPainterFontFile
  : IImageFormatReader<StarPainterFontFile>, IImageToRawImage<StarPainterFontFile> {

  /// <summary>Pixels across: thirty-two characters.</summary>
  public const int Width = 256;

  /// <summary>Rows: four character rows.</summary>
  public const int Height = 32;

  /// <summary>Characters across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Characters the set holds.</summary>
  public const int CharacterCount = 113;

  /// <summary>Bytes a character record occupies: eight rows and one the picture does not use.</summary>
  public const int CharacterLength = 9;

  /// <summary>Offset of the first character.</summary>
  public const int CharactersOffset = 3;

  /// <summary>The two bytes every file starts with — a load address rather than a signature.</summary>
  public static ReadOnlySpan<byte> Signature => [0xB0, 0xF0];

  /// <summary>Total file size.</summary>
  public const int FileSize = 1026;

  static string IImageFormatMetadata<StarPainterFontFile>.PrimaryExtension => ".zs";
  static string[] IImageFormatMetadata<StarPainterFontFile>.FileExtensions => [".zs"];
  static StarPainterFontFile IImageFormatReader<StarPainterFontFile>.FromSpan(ReadOnlySpan<byte> data)
    => StarPainterFontReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<StarPainterFontFile>.VideoModes => [
    new("Character set", [(Width, Height)], [2])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  private static readonly byte[] _Palette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(StarPainterFontFile file) {
    var data = file.Data ?? [];
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var character = (y >> 3) * Columns + (x >> 3);
      if (character >= CharacterCount)
        continue;

      var at = CharactersOffset + character * CharacterLength + (y & 7);
      if (at < data.Length && ((data[at] >> (~x & 7)) & 1) != 0)
        pixels[y * Width + x] = 1;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = (byte[])_Palette.Clone(),
      PaletteCount = 2,
    };
  }
}
