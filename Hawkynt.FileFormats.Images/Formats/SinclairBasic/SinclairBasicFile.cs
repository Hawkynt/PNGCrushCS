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
  : IImageFormatReader<SinclairBasicFile>, IImageToRawImage<SinclairBasicFile> {

  /// <summary>Where the program's first line sits in a saved memory image.</summary>
  public const int ProgramOffset = 116;

  static string IImageFormatMetadata<SinclairBasicFile>.PrimaryExtension => ".p";
  static string[] IImageFormatMetadata<SinclairBasicFile>.FileExtensions => [".p"];
  static SinclairBasicFile IImageFormatReader<SinclairBasicFile>.FromSpan(ReadOnlySpan<byte> data)
    => SinclairBasicReader.FromSpan(data);
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
}
