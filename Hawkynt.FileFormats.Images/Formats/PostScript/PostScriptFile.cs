using System;
using FileFormat.Core;

namespace FileFormat.PostScript;

/// <summary>A PostScript program (.ps, .eps) and the page it draws.</summary>
/// <remarks>
/// Reading is an interpreter for the drawing subset of the PostScript language. Writing uses a
/// standards-valid Level-1 <c>colorimage</c> program, so arbitrary raster images have a compact,
/// interoperable representation without pretending to reconstruct vector objects.
/// </remarks>
public readonly record struct PostScriptFile : IImageFormatReader<PostScriptFile>, IImageToRawImage<PostScriptFile>, IImageFromRawImage<PostScriptFile>, IImageFormatWriter<PostScriptFile> {

  static string IImageFormatMetadata<PostScriptFile>.PrimaryExtension => ".ps";
  static string[] IImageFormatMetadata<PostScriptFile>.FileExtensions => [
    ".ps", ".ps1", ".ps2", ".ps3", ".eps", ".epsf", ".epsi", ".epi", ".prn", ".pdx"
  ];

  static PostScriptFile IImageFormatReader<PostScriptFile>.FromSpan(ReadOnlySpan<byte> data) => PostScriptReader.FromSpan(data);
  static byte[] IImageFormatWriter<PostScriptFile>.ToBytes(PostScriptFile file) => PostScriptWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<PostScriptFile>.VideoModes => [
    new("Page", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<PostScriptFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 2)
      return null;
    if (header[0] == '%' && header[1] == '!')
      return true;
    return header.Length >= 4 && header[..4].SequenceEqual(PostScriptStructure.DosEpsMagic) ? null : false;
  }

  public byte[] Data { get; init; }
  public int Start { get; init; }
  public int End { get; init; }
  public PostScriptComments Comments { get; init; }

  public static RawImage ToRawImage(PostScriptFile file) => PostScriptRenderer.Render(file).Image;
  public static PostScriptFile FromRawImage(RawImage image) => PostScriptWriter.FromRawImage(image);
}
