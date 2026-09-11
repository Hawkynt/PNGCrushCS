using System;
using FileFormat.Core;

namespace FileFormat.EmbeddedDib;

/// <summary>A heuristic reader for drawing/project containers known to carry a Windows DIB preview somewhere inside.</summary>
/// <remarks>
/// This type does not claim that the extensions below share a container format. It preserves the old
/// preview-search behavior only for formats whose enclosing structure is not documented well enough
/// here to locate the preview deterministically.
/// <para/>
/// <c>.zmf</c> version 2 is an OLE2 compound document identified by its <c>Callisto_doc.zmf</c>
/// stream, but the preview stream/layout has not been verified. <c>.skf</c> and <c>.cad</c> are
/// AutoSketch and QuickCAD drawings, and <c>.btn</c> is a JustButtons animated bitmap. None of those
/// containers is a packed DIB file, so this remains read-only while <see cref="EmbeddedDibFile"/>
/// owns the actual <c>.dib</c> representation.
/// </remarks>
public readonly record struct EmbeddedDibPreviewFile
  : IImageFormatReader<EmbeddedDibPreviewFile>, IImageToRawImage<EmbeddedDibPreviewFile> {

  static string IImageFormatMetadata<EmbeddedDibPreviewFile>.PrimaryExtension => ".zmf";
  static string[] IImageFormatMetadata<EmbeddedDibPreviewFile>.FileExtensions => [".zmf", ".skf", ".cad", ".btn"];
  static EmbeddedDibPreviewFile IImageFormatReader<EmbeddedDibPreviewFile>.FromSpan(ReadOnlySpan<byte> data) {
    var embedded = EmbeddedDibReader.FindInSpan(data);
    return new() { Preview = embedded.Preview, Offset = embedded.Offset };
  }
  static VideoMode[] IImageFormatMetadata<EmbeddedDibPreviewFile>.VideoModes => [
    new("Preview", [(new IntegerRange(1, EmbeddedDibFile.MaxDimension), new IntegerRange(1, EmbeddedDibFile.MaxDimension))])
  ];

  /// <summary>The preview, already decoded by the bitmap reader.</summary>
  public RawImage Preview { get; init; }

  /// <summary>Where in the containing file the preview was found.</summary>
  public int Offset { get; init; }

  public static RawImage ToRawImage(EmbeddedDibPreviewFile file)
    => file.Preview ?? throw new InvalidOperationException("No preview was read.");
}
