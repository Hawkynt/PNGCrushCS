using System;
using FileFormat.Core;

namespace FileFormat.LarkaObjectEditor;

/// <summary>In-memory representation of a Larka Edytor Obiektów picture (.leo).</summary>
/// <remarks>
/// An ANTIC mode 4 object thirty-two characters wide and eight rows deep, drawn from two character
/// sets at once: even rows take their shapes from the first, odd rows from the second. Doubling the
/// set is what lets an object of this size use more distinct shapes than the 128 a single mode 4
/// character set holds.
/// <para/>
/// The character codes are not stored in reading order. They are interleaved so that the two sets'
/// halves sit apart, which is how the editor kept each set's codes contiguous in memory.
/// </remarks>
public readonly record struct LarkaObjectEditorFile
  : IImageFormatReader<LarkaObjectEditorFile>, IImageToRawImage<LarkaObjectEditorFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 256;

  /// <summary>Rows.</summary>
  public const int Height = 64;

  /// <summary>Characters across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Character rows.</summary>
  public const int CharacterRows = Height / 8;

  /// <summary>Size of one character set.</summary>
  public const int FontSize = 1024;

  /// <summary>Offset of the character codes, after the two character sets.</summary>
  public const int CharactersOffset = FontSize * 2;

  /// <summary>Offset of the five colour registers: PF0, PF1, PF2, PF3 and the background.</summary>
  public const int RegisterOffset = 2560;

  /// <summary>Total file size.</summary>
  public const int FileSize = 2580;

  static string IImageFormatMetadata<LarkaObjectEditorFile>.PrimaryExtension => ".leo";
  static string[] IImageFormatMetadata<LarkaObjectEditorFile>.FileExtensions => [".leo"];
  static LarkaObjectEditorFile IImageFormatReader<LarkaObjectEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => LarkaObjectEditorReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<LarkaObjectEditorFile>.VideoModes => [
    new("Object", [(Width, Height)], [5])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(LarkaObjectEditorFile file) {
    var data = file.Data ?? [];
    var registers = Atari8BitGraphics.ReadPf0123Bak(data, RegisterOffset);
    var frame = new byte[Width * Height];
    var characters = new byte[Columns];

    for (var row = 0; row < CharacterRows; ++row) {
      for (var column = 0; column < Columns; ++column) {
        var at = CharactersOffset + ((column & 1) << 7) + ((row & 1) << 6) + ((row & 6) << 3) + (column >> 1);
        characters[column] = at < data.Length ? data[at] : (byte)0;
      }

      // Even rows read the first character set, odd rows the second.
      Atari8BitGraphics.DecodeGr12Line(
        characters, 0, data, (row & 1) * FontSize, registers, frame, row * 8 * Width, Width, false);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }
}
