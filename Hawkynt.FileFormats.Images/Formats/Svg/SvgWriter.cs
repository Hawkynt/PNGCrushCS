using System;
using System.Text;
using System.Xml.Linq;

namespace FileFormat.Svg;

/// <summary>Serialises an SVG document.</summary>
public static class SvgWriter {

  public static byte[] ToBytes(SvgFile file) {
    var document = file.Document ?? throw new ArgumentException("No document to write.", nameof(file));

    using var text = new System.IO.MemoryStream();
    var settings = new System.Xml.XmlWriterSettings {
      Indent = true,
      Encoding = new UTF8Encoding(false),
      OmitXmlDeclaration = false,
    };

    using (var writer = System.Xml.XmlWriter.Create(text, settings))
      document.Save(writer);

    return text.ToArray();
  }

  /// <summary>A document holding one picture, placed at its own size.</summary>
  /// <remarks>
  /// An <c>image</c> element with a base64 data URI, which is what the specification provides for a
  /// drawing that carries a raster and what every renderer draws. The picture is embedded, not
  /// traced: turning a bitmap into paths would put outlines into the file that the picture never
  /// had, and a drawing of invented geometry filed under the original's name is worse than an honest
  /// rectangle of the pixels that were there.
  /// </remarks>
  public static XDocument Document(int width, int height, string dataUri) {
    ArgumentNullException.ThrowIfNull(dataUri);
    if (width < 1 || height < 1)
      throw new ArgumentOutOfRangeException(nameof(width), $"An SVG drawing of {width} by {height} has no picture in it.");

    XNamespace svg = SvgFile.Namespace;
    XNamespace xlink = "http://www.w3.org/1999/xlink";

    var image = new XElement(svg + "image",
      new XAttribute("x", 0),
      new XAttribute("y", 0),
      new XAttribute("width", width),
      new XAttribute("height", height),
      // Both spellings: href is what SVG 2 defines and xlink:href what every SVG 1.1 renderer reads.
      new XAttribute("href", dataUri),
      new XAttribute(xlink + "href", dataUri),
      new XAttribute("image-rendering", "pixelated"),
      new XAttribute("preserveAspectRatio", "none"));

    var root = new XElement(svg + "svg",
      new XAttribute(XNamespace.Xmlns + "xlink", xlink.NamespaceName),
      new XAttribute("version", "1.1"),
      new XAttribute("width", width),
      new XAttribute("height", height),
      new XAttribute("viewBox", $"0 0 {width} {height}"),
      image);

    return new(new XDeclaration("1.0", "UTF-8", null), root);
  }
}
