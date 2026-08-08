using System;
using System.Xml.Linq;
using FileFormat.Core;

namespace FileFormat.Svg;

/// <summary>A Scalable Vector Graphics drawing (.svg).</summary>
/// <remarks>
/// An XML document whose root is <c>&lt;svg&gt;</c>, holding shapes, groups and the properties that
/// paint them. What is read here is the geometry and the paint: paths, rectangles, circles,
/// ellipses, lines, polylines and polygons; groups, <c>use</c> and <c>symbol</c>; transforms;
/// presentation properties whether written as attributes, in a <c>style</c> attribute or in a
/// <c>&lt;style&gt;</c> element; linear and radial gradients; and clipping paths.
/// <para/>
/// The size is the drawing's own. Its <c>width</c> and <c>height</c> where it states them —
/// converted at the ninety-six pixels to the inch the specification defines the pixel as, which is
/// what makes <c>4.1in</c> come out at 394 and <c>1920pt</c> at 2560 — otherwise its
/// <c>viewBox</c>, and otherwise the box its own contents fall in, which is what a renderer has
/// left when the file states nothing.
/// <para/>
/// A raster is read where the document carries one outright: an <c>image</c> whose <c>href</c> is a
/// <c>data:</c> URI holding a PNG or a JPEG. One that names a file or a URL is not fetched — that
/// would be a picture opening a network connection or reading a path somebody else chose — so its
/// rectangle is left empty.
/// <para/>
/// What is not read: text, filters, markers, patterns and animation. Text needs fonts that are not
/// in the file, and the rest change how a shape looks rather than where it is. A drawing that is
/// nothing but text therefore comes out blank rather than wrong, and the four samples here that
/// carry any have it as labelling on drawings that are otherwise geometry.
/// <para/>
/// Writing embeds rather than traces. A picture goes out as one <c>image</c> element carrying it as
/// a base64 PNG, at its own size, which is a conforming drawing that any renderer draws. Turning a
/// bitmap into paths would put geometry into the file that the picture never had.
/// </remarks>
public readonly record struct SvgFile
  : IImageFormatReader<SvgFile>, IImageToRawImage<SvgFile>,
    IImageFromRawImage<SvgFile>, IImageFormatWriter<SvgFile> {

  /// <summary>The namespace every conforming drawing puts its elements in.</summary>
  public const string Namespace = "http://www.w3.org/2000/svg";

  static string IImageFormatMetadata<SvgFile>.PrimaryExtension => ".svg";
  static string[] IImageFormatMetadata<SvgFile>.FileExtensions => [".svg"];
  static SvgFile IImageFormatReader<SvgFile>.FromSpan(ReadOnlySpan<byte> data) => SvgReader.FromSpan(data);
  static byte[] IImageFormatWriter<SvgFile>.ToBytes(SvgFile file) => SvgWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<SvgFile>.VideoModes => [
    new("Drawing", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>The document, as it was parsed.</summary>
  public XDocument Document { get; init; }

  /// <summary>The root element, which is what everything is drawn from.</summary>
  public XElement Root => this.Document?.Root ?? throw new InvalidOperationException("No document was read.");

  public static RawImage ToRawImage(SvgFile file) => SvgRenderer.Render(file);

  /// <summary>A drawing that holds this picture, at its own size, as an embedded PNG.</summary>
  public static SvgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Document = SvgWriter.Document(image.Width, image.Height, SvgDataUri.EncodePng(image)) };
  }
}
