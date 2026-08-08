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
/// What is not read: text, filters, markers, patterns, animation and raster images. Text needs
/// fonts that are not in the file, and the rest change how a shape looks rather than where it is.
/// A drawing that is nothing but text therefore comes out blank rather than wrong, and the four
/// samples here that carry any have it as labelling on drawings that are otherwise geometry.
/// <para/>
/// It does not write.
/// </remarks>
public readonly record struct SvgFile : IImageFormatReader<SvgFile>, IImageToRawImage<SvgFile> {

  /// <summary>The namespace every conforming drawing puts its elements in.</summary>
  public const string Namespace = "http://www.w3.org/2000/svg";

  static string IImageFormatMetadata<SvgFile>.PrimaryExtension => ".svg";
  static string[] IImageFormatMetadata<SvgFile>.FileExtensions => [".svg"];
  static SvgFile IImageFormatReader<SvgFile>.FromSpan(ReadOnlySpan<byte> data) => SvgReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SvgFile>.VideoModes => [
    new("Drawing", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>The document, as it was parsed.</summary>
  public XDocument Document { get; init; }

  /// <summary>The root element, which is what everything is drawn from.</summary>
  public XElement Root => this.Document?.Root ?? throw new InvalidOperationException("No document was read.");

  public static RawImage ToRawImage(SvgFile file) => SvgRenderer.Render(file);
}
