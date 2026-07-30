using System;
using FileFormat.Core;

namespace FileFormat.GraphLogo;

/// <summary>In-memory representation of a Graph picture (.all).</summary>
/// <remarks>
/// A full ANTIC mode 4 screen that switches character set every row. The file starts with
/// twenty-four bank numbers, one per character row, followed by however many one-kilobyte sets
/// those numbers refer to, then the screen's characters and its colours. Redefining the set between
/// rows is what lets a mode 4 screen carry more than 128 distinct shapes: each row gets its own
/// alphabet.
/// </remarks>
public readonly record struct GraphLogoFile
  : IImageFormatReader<GraphLogoFile>, IImageToRawImage<GraphLogoFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Characters across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Character rows, each with its own set.</summary>
  public const int CharacterRows = Height / 8;

  /// <summary>Size of one character set.</summary>
  public const int FontSize = 1024;

  /// <summary>Offset of the first character set, after the per-row bank numbers.</summary>
  public const int FontOffset = CharacterRows;

  /// <summary>Bytes that follow the character sets: the screen and then five colour registers.</summary>
  public const int TrailerSize = Columns * CharacterRows + 5;

  /// <summary>What a file's length is congruent to, modulo the character set size.</summary>
  public const int LengthRemainder = FontOffset + TrailerSize;

  static string IImageFormatMetadata<GraphLogoFile>.PrimaryExtension => ".all";
  static string[] IImageFormatMetadata<GraphLogoFile>.FileExtensions => [".all"];
  static GraphLogoFile IImageFormatReader<GraphLogoFile>.FromSpan(ReadOnlySpan<byte> data)
    => GraphLogoReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<GraphLogoFile>.VideoModes => [
    new("Graph", [(Width, Height)], [5])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(GraphLogoFile file) {
    var data = file.Data ?? [];
    var screenOffset = data.Length - TrailerSize;
    var registers = Atari8BitGraphics.ReadPf0123Bak(data, data.Length - 5);
    var frame = new byte[Width * Height];

    for (var row = 0; row < CharacterRows; ++row) {
      var bank = row < data.Length ? data[row] : 0;
      Atari8BitGraphics.DecodeGr12Line(
        data, screenOffset + row * Columns, data, FontOffset + bank * FontSize,
        registers, frame, row * 8 * Width, Width, false);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }
}
