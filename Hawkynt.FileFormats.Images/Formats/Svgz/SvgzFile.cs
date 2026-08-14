using System;
using FileFormat.Core;
using FileFormat.Svg;

namespace FileFormat.Svgz;

/// <summary>A Scalable Vector Graphics drawing under gzip (.svgz).</summary>
/// <remarks>
/// The whole of the format is the wrapper. What is inside is an ordinary <c>.svg</c>, so everything
/// about how a drawing is measured and painted is <see cref="SvgFile"/>'s and nothing about it is
/// repeated here; this reads the gzip off and hands the bytes over.
/// <para/>
/// The name is the only thing that says a gzipped file holds a drawing — gzip's two magic bytes are
/// the same whatever was compressed — so detection from bytes has to open the stream far enough to
/// see the document inside. That is what <see cref="MatchesSignature"/> does, and why it answers
/// for a gzipped drawing and not for a gzipped anything-else.
/// </remarks>
public readonly record struct SvgzFile
  : IImageFormatReader<SvgzFile>, IImageToRawImage<SvgzFile>,
    IImageFromRawImage<SvgzFile>, IImageFormatWriter<SvgzFile> {

  /// <summary>Gzip's own two bytes, which are all a header states about the wrapper.</summary>
  internal const byte Magic0 = 0x1F;

  /// <summary>The second of gzip's two bytes.</summary>
  internal const byte Magic1 = 0x8B;

  static string IImageFormatMetadata<SvgzFile>.PrimaryExtension => ".svgz";
  static string[] IImageFormatMetadata<SvgzFile>.FileExtensions => [".svgz"];
  static SvgzFile IImageFormatReader<SvgzFile>.FromSpan(ReadOnlySpan<byte> data) => SvgzReader.FromSpan(data);
  static byte[] IImageFormatWriter<SvgzFile>.ToBytes(SvgzFile file) => SvgzWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SvgzFile>.VideoModes => [
    new("Drawing", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>
  /// Whether these bytes are a gzipped drawing, judged by opening the stream rather than by its
  /// wrapper.
  /// </summary>
  /// <remarks>
  /// A detector is handed a header rather than a file, so the deflate stream runs out part way
  /// through; that is expected and whatever came out before it did is what gets looked at. Sixty-odd
  /// bytes of gzip unpack to several hundred of XML, which reaches the root element of every drawing
  /// that does not open with an unusually long comment.
  /// </remarks>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 3 || header[0] != Magic0 || header[1] != Magic1)
      return false;

    var text = SvgzReader.PeekText(header);

    // Nothing came out: the header was too short to reach any of the document, so this says neither
    // yes nor no rather than claiming a file it has not seen inside of.
    return text.Length == 0 ? null : text.Contains("<svg", StringComparison.Ordinal) ? true : null;
  }

  /// <summary>The drawing that was compressed.</summary>
  public SvgFile Drawing { get; init; }

  public static RawImage ToRawImage(SvgzFile file) => SvgFile.ToRawImage(file.Drawing);

  /// <summary>A gzipped drawing holding this picture, at its own size, as an embedded PNG.</summary>
  public static SvgzFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Drawing = SvgFile.FromRawImage(image) };
  }
}
