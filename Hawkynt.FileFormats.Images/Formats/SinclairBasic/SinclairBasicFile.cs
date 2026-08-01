using System;
using FileFormat.Core;

namespace FileFormat.SinclairBasic;

/// <summary>In-memory representation of a Sinclair BASIC picture program (.p).</summary>
/// <remarks>
/// Not a picture format but a saved ZX81 program that draws one. The machine had no way to store an
/// image on its own, so a picture was distributed as a BASIC listing whose PRINT statements put the
/// block characters where they belonged, and running the program was how you looked at it.
/// <para/>
/// What is here is therefore not an interpreter but a reader of the one shape those programs take:
/// PRINT AT and a string, optionally a scrolling bottom line assembled from a fixed sequence of
/// LET, FOR, POKE and NEXT. Anything outside that shape is rejected rather than guessed at, which
/// is the right trade — a program that does something else is a program, not a picture.
/// </remarks>
public readonly record struct SinclairBasicFile
  : IImageFormatReader<SinclairBasicFile>, IImageToRawImage<SinclairBasicFile>,
    IImageFromRawImage<SinclairBasicFile>, IImageFormatWriter<SinclairBasicFile> {

  /// <summary>Where the program's first line sits in a saved memory image.</summary>
  public const int ProgramOffset = 116;

  static string IImageFormatMetadata<SinclairBasicFile>.PrimaryExtension => ".p";
  static string[] IImageFormatMetadata<SinclairBasicFile>.FileExtensions => [".p"];
  static SinclairBasicFile IImageFormatReader<SinclairBasicFile>.FromSpan(ReadOnlySpan<byte> data)
    => SinclairBasicReader.FromSpan(data);
  static byte[] IImageFormatWriter<SinclairBasicFile>.ToBytes(SinclairBasicFile file)
    => SinclairBasicWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SinclairBasicFile>.VideoModes => [
    new("ZX81", [(Zx81Graphics.Width, Zx81Graphics.Height)], [2])
  ];

  /// <summary>The screen the program leaves behind.</summary>
  public byte[] Screen { get; init; }

  public static RawImage ToRawImage(SinclairBasicFile file) => new() {
    Width = Zx81Graphics.Width,
    Height = Zx81Graphics.Height,
    Format = PixelFormat.Indexed8,
    PixelData = Zx81Graphics.Decode(file.Screen ?? []),
    Palette = Zx81Graphics.CreatePalette(),
    PaletteCount = 2,
  };

  /// <summary>Builds the screen a program would draw, from the machine's own character shapes.</summary>
  /// <remarks>
  /// The ZX81 draws with characters and nothing else, so a picture is made of its sixty-four
  /// shapes, each optionally inverted. Every cell is matched against all of them and the closest
  /// kept — which is exact for a picture already made of them, and the best available for one that
  /// is not.
  /// <para/>
  /// The picture is taken at the twenty-two rows a PRINT statement can reach. The two below them
  /// are the machine's input area and need a scroll routine to fill, so a source that is taller is
  /// sampled into those rows rather than losing its bottom silently.
  /// </remarks>
  public static SinclairBasicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    const int rows = SinclairBasicWriter.LastPrintableRow + 1;
    var height = rows * 8;

    var set = GlyphSheet.Sample(image, Zx81Graphics.Width, height);
    var wanted = new byte[Zx81Graphics.Width * height];
    for (var i = 0; i < wanted.Length; ++i)
      wanted[i] = (byte)(set[i] ? 1 : 0);

    var matched = CharacterRoms.MatchGlyphs(wanted, Zx81Graphics.Columns, rows, CharacterRoms.Zx81, 64);

    // The screen is the full twenty-four rows; the last two stay blank, which is what a program
    // without a scroll routine leaves behind.
    var screen = new byte[Zx81Graphics.ScreenSize];
    matched.AsSpan(0, Math.Min(matched.Length, screen.Length)).CopyTo(screen);

    return new() { Screen = screen };
  }
}
