using System;
using FileFormat.Core;

namespace FileFormat.DaisyDotFont;

/// <summary>In-memory representation of a Daisy-Dot font (.nlq).</summary>
/// <remarks>
/// A near-letter-quality font for a dot-matrix printer rather than a screen, which is why its
/// glyphs are stored sideways: a character is a run of columns, two bytes each, because that is the
/// order the print head fires. Each byte covers eight of the sixteen rows and the two interleave,
/// the first byte giving the even rows and the second the odd — the two passes the printer made
/// over each line to get twice the vertical resolution its pins allowed.
/// <para/>
/// Characters are variable width and stored one after another with no index, so the only way to the
/// ninety-first is through the ninety before it. Shown here as a sixteen-by-six grid in the machine's
/// own character order, which is why the codes skip.
/// </remarks>
public readonly record struct DaisyDotFontFile
  : IImageFormatReader<DaisyDotFontFile>, IImageToRawImage<DaisyDotFontFile>,
    IImageFromRawImage<DaisyDotFontFile>, IImageFormatWriter<DaisyDotFontFile> {

  /// <summary>The text every file starts with.</summary>
  public const string Signature = "DAISY-DOT NLQ FONT";

  /// <summary>The byte that ends a line on this machine, and every character record here.</summary>
  public const byte Terminator = 155;

  /// <summary>Offset of the first character.</summary>
  public const int CharactersOffset = 19;

  /// <summary>Characters a font holds.</summary>
  public const int CharacterCount = 91;

  /// <summary>Widest character the format allows.</summary>
  public const int MaxCharacterWidth = 19;

  /// <summary>Screen pixels a cell occupies.</summary>
  public const int CellWidth = 20;

  /// <summary>Rows a cell occupies.</summary>
  public const int CellHeight = 16;

  /// <summary>Cells across.</summary>
  public const int Columns = 16;

  /// <summary>Pixels across.</summary>
  public const int Width = Columns * CellWidth;

  /// <summary>Rows.</summary>
  public const int Height = 96;

  /// <summary>The luminance a lit pixel takes.</summary>
  public const byte Ink = 14;

  static string IImageFormatMetadata<DaisyDotFontFile>.PrimaryExtension => ".nlq";
  static string[] IImageFormatMetadata<DaisyDotFontFile>.FileExtensions => [".nlq"];
  static DaisyDotFontFile IImageFormatReader<DaisyDotFontFile>.FromSpan(ReadOnlySpan<byte> data)
    => DaisyDotFontReader.FromSpan(data);
  static byte[] IImageFormatWriter<DaisyDotFontFile>.ToBytes(DaisyDotFontFile file)
    => DaisyDotFontWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<DaisyDotFontFile>.VideoModes => [
    new("Font", [(Width, Height)], [2])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(DaisyDotFontFile file) {
    var data = file.Data ?? [];
    var frame = new byte[Width * Height];
    var offset = CharactersOffset;

    for (var i = 0; i < CharacterCount && offset < data.Length; ++i) {
      var characterWidth = data[offset];

      // The codes run 0 to 63, skip one, then 65 to 90, and end at 92.
      var code = i < 64 ? i : i < 90 ? i + 1 : 92;

      for (var y = 0; y < CellHeight; ++y)
      for (var x = 0; x < characterWidth; ++x) {
        var at = offset + 1 + (y & 1) * characterWidth + x;
        if (at >= data.Length)
          break;

        // The two passes interleave, and a byte's bits run down the column from the top.
        var lit = ((data[at] >> (7 - (y >> 1))) & 1) != 0;
        frame[((code & 240) | y) * Width + (code & 15) * CellWidth + x] = lit ? Ink : (byte)0;
      }

      offset += (characterWidth + 1) * 2;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }

  /// <summary>Which character code a position in the sheet carries.</summary>
  /// <remarks>The codes run 0 to 63, skip one, then 65 to 90, and end at 92.</remarks>
  public static int CodeAt(int index) => index < 64 ? index : index < 90 ? index + 1 : 92;

  /// <summary>Builds a font from the sheet, every character at the format's widest.</summary>
  /// <remarks>
  /// Characters are variable width and stored one after another with no index, so a reader reaches
  /// the ninety-first only through the ninety before it. Writing them all at the maximum width
  /// costs a little space and removes the question entirely: nothing has to be measured, and a
  /// character that happens to be blank on its right is simply blank there.
  /// <para/>
  /// A character's two passes interleave by row — even rows in the first, odd in the second — and
  /// within a byte the bits run down the column from the top rather than across.
  /// </remarks>
  public static DaisyDotFontFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var set = GlyphSheet.Sample(image, Width, Height);
    var record = (MaxCharacterWidth + 1) * 2;
    var data = new byte[CharactersOffset + CharacterCount * record];
    DaisyDotFontWriter.WriteHeader(data);

    for (var i = 0; i < CharacterCount; ++i) {
      var offset = CharactersOffset + i * record;
      data[offset] = MaxCharacterWidth;

      var code = CodeAt(i);
      var left = (code & 15) * CellWidth;
      var top = code & 240;

      for (var y = 0; y < CellHeight; ++y)
      for (var x = 0; x < MaxCharacterWidth; ++x) {
        if (!set[(top + y) * Width + left + x])
          continue;

        data[offset + 1 + (y & 1) * MaxCharacterWidth + x] |= (byte)(1 << (7 - (y >> 1)));
      }

      data[offset + record - 1] = Terminator;
    }

    return new() { Data = data };
  }
}
