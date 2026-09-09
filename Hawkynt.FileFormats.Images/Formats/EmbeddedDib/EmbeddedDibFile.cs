using System;
using FileFormat.Core;

namespace FileFormat.EmbeddedDib;

/// <summary>The Windows bitmap preview carried inside a drawing or a project file whose container is not yet bounded.</summary>
/// <remarks>
/// This is deliberately a heuristic preview reader, not a claim that the remaining extensions share
/// a container format. Each name stays here only until its own structure can be verified well enough
/// to identify the container before looking for a DIB.
/// <para/>
/// <c>.zmf</c> version 2 is known to be an OLE2 compound file and PRONOM identifies it by the
/// <c>Callisto_doc.zmf</c> stream, but no source checked for this implementation identifies the
/// preview stream/layout; libzmf covers the later Zoner Draw generations instead. <c>.skf</c> and
/// <c>.cad</c> are externally identified as Autodesk SKETCH and QuickCAD thumbnails, and <c>.btn</c>
/// as a JustButtons animated bitmap, but their enclosing layouts are not published well enough to
/// replace a byte search with a container parser. They therefore remain here rather than inheriting
/// guessed structures from CDR, CMX, SDG or IPG.
/// <para/>
/// A related <c>.jig</c> is left out deliberately. It carries a header of the right shape at 14 and
/// its picture where one would expect, but it states no colour count and keeps no palette anywhere
/// in the file — searching every offset for one, in either byte order and at three or four bytes an
/// entry, accounts for 13 of the 181 colours the tool draws. Whatever supplies the rest is not in
/// the file, so it is refused rather than drawn in the wrong colours.
/// <para/>
/// It is not registered as a whole-file writer. What it read was a preview inside somebody else's
/// file, and emitting one alone would produce something no drawing program would open. Container
/// writers can use <see cref="EmbeddedDibWriter"/> to serialize the packed DIB payload they embed.
/// </remarks>
public readonly record struct EmbeddedDibFile
  : IImageFormatReader<EmbeddedDibFile>, IImageToRawImage<EmbeddedDibFile> {

  /// <summary>The shortest and longest <c>BITMAPINFOHEADER</c> this accepts.</summary>
  public const int MinHeaderSize = 40, MaxHeaderSize = 124;

  /// <summary>No picture in these previews comes near this, and it keeps a false match cheap.</summary>
  public const int MaxDimension = 20000;

  static string IImageFormatMetadata<EmbeddedDibFile>.PrimaryExtension => ".zmf";
  static string[] IImageFormatMetadata<EmbeddedDibFile>.FileExtensions => [".zmf", ".skf", ".cad", ".btn"];
  static EmbeddedDibFile IImageFormatReader<EmbeddedDibFile>.FromSpan(ReadOnlySpan<byte> data)
    => EmbeddedDibReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<EmbeddedDibFile>.VideoModes => [
    new("Preview", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))])
  ];

  /// <summary>The preview, already decoded by the bitmap reader.</summary>
  public RawImage Preview { get; init; }

  /// <summary>Where in the containing file the preview was found.</summary>
  public int Offset { get; init; }

  public static RawImage ToRawImage(EmbeddedDibFile file)
    => file.Preview ?? throw new InvalidOperationException("No preview was read.");
}
