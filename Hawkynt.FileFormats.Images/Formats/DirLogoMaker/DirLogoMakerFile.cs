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
  : IImageFormatReader<DirLogoMakerFile>, IImageToRawImage<DirLogoMakerFile>,
    IImageFromRawImage<DirLogoMakerFile>, IImageFormatWriter<DirLogoMakerFile> {

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
  static byte[] IImageFormatWriter<DirLogoMakerFile>.ToBytes(DirLogoMakerFile file)
    => DirLogoMakerWriter.ToBytes(file);
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

  /// <summary>Builds a logo from a picture, one character shape at a time.</summary>
  /// <remarks>
  /// Eleven characters across and sixteen down is not a design choice but what a directory listing
  /// gave: the width of a filename and one row per entry. A picture of any other shape is scaled
  /// into it, there being nowhere else for it to go.
  /// </remarks>
  public static DirLogoMakerFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var wanted = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var sourceX = image.Width == Width ? x : x * image.Width / Width;
      var sourceY = image.Height == Height ? y : y * image.Height / Height;
      var source = (sourceY * image.Width + sourceX) * 3;

      var luminance = rgb.PixelData[source] * 77 + rgb.PixelData[source + 1] * 150 + rgb.PixelData[source + 2] * 29;

      // A set bit shows the foreground, which in this mode is the lighter of the two.
      wanted[y * Width + x] = (byte)(luminance >= 128 * 256 ? 1 : 0);
    }

    return new() { Characters = CharacterRoms.MatchGlyphs(wanted, Columns, Rows, CharacterRoms.Atari8, 128) };
  }
}
