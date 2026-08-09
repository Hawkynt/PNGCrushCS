using System;
using FileFormat.Core;

namespace FileFormat.PostScript;

/// <summary>A PostScript program (.ps, .eps) and the page it draws.</summary>
/// <remarks>
/// Built from Adobe's <em>PostScript Language Reference Manual</em>, third edition — chapter 3 for
/// the interpreter and its stacks, chapter 4 for the graphics, chapter 8 for the operators — and
/// from the <em>Document Structuring Conventions Specification</em>, version 3.0, for the comments
/// that say how big the page is.
/// <para/>
/// PostScript is a programming language rather than a file format, so this is an interpreter: the
/// program is scanned into objects, run on an operand stack against a stack of dictionaries, and
/// what it draws goes onto the vector rasteriser this tree already has. What is implemented is the
/// part a drawing uses — paths, transforms, colour in grey, RGB and CMYK, clipping, images, and the
/// control constructs and data types the programs are written in.
/// <para/>
/// A subset is stated rather than pretended at. An operator that is not implemented is not defined,
/// and a program that reaches one stops with its name in the message rather than carrying on with
/// the operand it left behind — a skipped colour operator is how a figure ends up black. What a
/// program guards with <c>stopped</c> or asks about with <c>where</c> is still answered the way the
/// language says, because that is the program testing its ground rather than going wrong.
/// <para/>
/// Text is not drawn. A file names a font; the glyphs are in the font and not in the file, and this
/// carries no font library. Drawing a box where the words are would put geometry on the page that
/// the file never stated, so the text operators consume their operands and mark nothing — the same
/// decision the SVG, HP-GL and DXF readers here make. A page that is nothing but words therefore
/// comes out blank rather than wrong.
/// <para/>
/// The first page is drawn and the program stops at the first <c>showpage</c>: a raster is one
/// picture and the second page would draw over the first.
/// <para/>
/// The size is the <c>%%BoundingBox</c> the file states, or its <c>%%HiResBoundingBox</c> where
/// there is no other, or the default US Letter page of 612 by 792 points where the file states
/// neither; either way it is rendered at ninety-six pixels to the inch, and
/// <see cref="PostScriptRendering.SizeSource"/> says which of the three was used.
/// <para/>
/// It does not write.
/// </remarks>
public readonly record struct PostScriptFile : IImageFormatReader<PostScriptFile>, IImageToRawImage<PostScriptFile> {

  static string IImageFormatMetadata<PostScriptFile>.PrimaryExtension => ".ps";

  /// <summary>
  /// <c>.pdx</c> is Mayura Draw, which saves Encapsulated PostScript under a name of its own.
  /// </summary>
  /// <remarks>
  /// The drawing program formerly called PageDraw writes <c>.pdx</c> and <c>.md</c> files that open
  /// <c>%!PS-Adobe-3.0 EPSF-3.0</c> — XnView catalogues the name separately and reads it with the
  /// same interpreter it reads <c>.eps</c> with, which is what handing it the same file under both
  /// names shows. Claiming the name costs nothing in strictness: what decides is still the two
  /// characters the program has to begin with.
  /// </remarks>
  static string[] IImageFormatMetadata<PostScriptFile>.FileExtensions => [
    ".ps", ".ps1", ".ps2", ".ps3", ".eps", ".epsf", ".epsi", ".epi", ".prn", ".pdx"
  ];

  static PostScriptFile IImageFormatReader<PostScriptFile>.FromSpan(ReadOnlySpan<byte> data) => PostScriptReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<PostScriptFile>.VideoModes => [
    new("Page", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<PostScriptFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 2)
      return null;

    // Every PostScript program opens with a percent sign and an exclamation mark. A file wrapped for
    // a PC opens with its own four bytes instead and carries the program inside, and those four
    // bytes belong to the reader that takes the preview out of such a file, so this has no opinion
    // on them rather than an argument about them.
    if (header[0] == '%' && header[1] == '!')
      return true;

    return header.Length >= 4 && header[..4].SequenceEqual(PostScriptStructure.DosEpsMagic) ? null : false;
  }

  /// <summary>The bytes of the file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Where the program starts in them.</summary>
  public int Start { get; init; }

  /// <summary>Where it ends.</summary>
  public int End { get; init; }

  /// <summary>What the structuring comments say.</summary>
  public PostScriptComments Comments { get; init; }

  public static RawImage ToRawImage(PostScriptFile file) => PostScriptRenderer.Render(file).Image;
}
