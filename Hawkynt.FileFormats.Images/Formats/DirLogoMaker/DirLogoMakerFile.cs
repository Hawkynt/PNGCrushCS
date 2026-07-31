using System;
using FileFormat.Core;

namespace FileFormat.DirLogoMaker;

/// <summary>In-memory representation of a Dir Logo Maker logo (.dlm).</summary>
/// <remarks>
/// The logo a disk directory listing showed, which is why it is the shape it is: eleven characters
/// across, that being what fits in a filename field, and sixteen down, one per directory entry. The
/// file is the sixteen entries as the disk held them, so each row's eleven characters sit five
/// bytes into a sixteen-byte record and the rest is the file size and flags nobody drew.
/// <para/>
/// The characters are stored as ASCII and have to be translated into the machine's own order, which
/// is not the same: its character set puts the punctuation before the letters.
/// </remarks>
public readonly record struct DirLogoMakerFile
  : IImageFormatReader<DirLogoMakerFile>, IImageToRawImage<DirLogoMakerFile> {

  /// <summary>Character cells across, which is the width of a filename.</summary>
  public const int Columns = 11;

  /// <summary>Directory entries, one to a row.</summary>
  public const int Rows = 16;

  /// <summary>Bytes one directory entry occupies.</summary>
  public const int EntrySize = 16;

  /// <summary>Where the name starts within an entry.</summary>
  public const int NameOffset = 5;

  /// <summary>Total file size.</summary>
  public const int FileSize = Rows * EntrySize;

  /// <summary>Pixels across.</summary>
  public const int Width = Columns * 8;

  /// <summary>Rows of pixels.</summary>
  public const int Height = Rows * 8;

  static string IImageFormatMetadata<DirLogoMakerFile>.PrimaryExtension => ".dlm";
  static string[] IImageFormatMetadata<DirLogoMakerFile>.FileExtensions => [".dlm"];
  static DirLogoMakerFile IImageFormatReader<DirLogoMakerFile>.FromSpan(ReadOnlySpan<byte> data)
    => DirLogoMakerReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DirLogoMakerFile>.VideoModes => [
    new("Atari 8-bit", [(Width, Height)], [2])
  ];

  /// <summary>The character codes, already translated into the machine's order.</summary>
  public byte[] Characters { get; init; }

  public static RawImage ToRawImage(DirLogoMakerFile file) {
    var frame = new byte[Width * Height];
    CharacterRoms.DecodeGraphics0(file.Characters ?? [], 0, Columns, CharacterRoms.Atari8, frame, Width, Height);

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }
}
