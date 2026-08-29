using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.PostScript;

namespace FileFormat.Illustrator;

/// <summary>An Adobe Illustrator drawing (.ai).</summary>
/// <remarks>
/// Illustrator 5-8 files are PostScript-language documents with native Illustrator operators. This
/// implementation now reads and writes Adobe's documented Illustrator-6 <c>XI</c> raster object
/// directly, while retaining the PostScript renderer for vector files whose required procsets are
/// present. Version 9 and newer PDF-based AI belongs to the PDF reader.
/// </remarks>
public readonly record struct AiFile :
  IImageFormatReader<AiFile>, IImageToRawImage<AiFile>, IImageFromRawImage<AiFile>, IImageFormatWriter<AiFile> {

  static string IImageFormatMetadata<AiFile>.PrimaryExtension => ".ai";
  static string[] IImageFormatMetadata<AiFile>.FileExtensions => [".ai"];
  static AiFile IImageFormatReader<AiFile>.FromSpan(ReadOnlySpan<byte> data) => AiReader.FromSpan(data);
  static byte[] IImageFormatWriter<AiFile>.ToBytes(AiFile file) => AiWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<AiFile>.VideoModes => [
    new("Native XI raster", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<AiFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;
    if (header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F')
      return false;
    return header[0] == '%' && header[1] == '!' ? true : null;
  }

  /// <summary>The program, as PostScript, for ordinary pre-9 Illustrator vector files.</summary>
  public PostScriptFile? Program { get; init; }

  /// <summary>A native embedded XI raster object, when the Illustrator document contains one.</summary>
  public RawImage? Raster { get; init; }

  /// <summary>Which version of Illustrator wrote it, out of its own comment, or nothing where it does not say.</summary>
  public string? Version { get; init; }

  /// <summary>The procedure sets it needs and does not carry, which is why a vector program would be refused.</summary>
  public IReadOnlyList<string> MissingProcedureSets
    => this.Program is { } program ? program.Comments.MissingProcedureSets : [];

  public static RawImage ToRawImage(AiFile file) {
    if (file.Raster != null)
      return file.Raster;
    if (file.Program is { } program)
      return PostScriptRenderer.Render(program).Image;
    throw new InvalidOperationException("An Illustrator file carries neither a native raster nor a readable PostScript program.");
  }

  public static AiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Raster = image.EnsureFormat(PixelFormat.Rgb24), Version = "AI5_FileFormat 2.0 (Illustrator 6.0)" };
  }
}
