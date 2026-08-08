using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.PostScript;

namespace FileFormat.Illustrator;

/// <summary>An Adobe Illustrator drawing (.ai).</summary>
/// <remarks>
/// Built from the <em>Adobe Illustrator File Format Specification</em> — the document that describes
/// what an <c>.ai</c> file is — together with Adobe's PostScript reference, which is the language it
/// is written in up to version 8.
/// <para/>
/// Up to version 8 an Illustrator file is an encapsulated PostScript program, so it is read by the
/// PostScript interpreter in this tree. From version 9 on it is a PDF file that happens to be named
/// <c>.ai</c>, and it is refused here by name: its first four bytes say <c>%PDF</c>, and the reader
/// for that is the PDF one, which the same four bytes route it to.
/// <para/>
/// The operators an Illustrator file draws with — <c>m</c> for a move, <c>l</c> and <c>c</c> for a
/// line and a curve, <c>f</c>, <c>S</c>, <c>b</c> and the rest for the ways of painting them, <c>k</c>
/// and <c>g</c> for colour, <c>Ap</c> and <c>Ar</c> and the other <c>A</c> operators for the
/// annotations — are not operators of the language. They are defined in procedure sets called
/// <c>Adobe_Illustrator_AI5</c>, <c>Adobe_level2_AI5</c> and their relatives, and a file either
/// carries those definitions or declares that it needs them from elsewhere.
/// <para/>
/// A file that declares it needs a procedure set it does not carry is refused, by the name of the
/// set. That is the whole of what makes this reader worth having: without those definitions the
/// meaning of every operator in the drawing is unknown, and drawing it anyway means inventing a
/// meaning — which is how figures come out in a colour the file never asked for. Ghostscript refuses
/// such a file with <c>undefined in Adobe_level2_AI5</c>, for the same reason and at the same point.
/// <para/>
/// Text is not drawn, and the first page is the one rendered; both for the reasons set out on
/// <see cref="PostScriptFile"/>.
/// <para/>
/// It does not write.
/// </remarks>
public readonly record struct AiFile : IImageFormatReader<AiFile>, IImageToRawImage<AiFile> {

  static string IImageFormatMetadata<AiFile>.PrimaryExtension => ".ai";
  static string[] IImageFormatMetadata<AiFile>.FileExtensions => [".ai"];
  static AiFile IImageFormatReader<AiFile>.FromSpan(ReadOnlySpan<byte> data) => AiReader.FromSpan(data);

  static VideoMode[] IImageFormatMetadata<AiFile>.VideoModes => [
    new("Artwork", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<AiFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;

    // A PDF under this name is version 9 or later and belongs to the PDF reader, which the same
    // four bytes take it to. Saying no here rather than nothing is what makes that happen.
    if (header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F')
      return false;

    return header[0] == '%' && header[1] == '!' ? true : null;
  }

  /// <summary>The program, as PostScript.</summary>
  public PostScriptFile Program { get; init; }

  /// <summary>Which version of Illustrator wrote it, out of its own comment, or nothing where it does not say.</summary>
  public string? Version { get; init; }

  /// <summary>The procedure sets it needs and does not carry, which is why it would be refused.</summary>
  public IReadOnlyList<string> MissingProcedureSets => this.Program.Comments.MissingProcedureSets;

  public static RawImage ToRawImage(AiFile file) => PostScriptRenderer.Render(file.Program).Image;
}
