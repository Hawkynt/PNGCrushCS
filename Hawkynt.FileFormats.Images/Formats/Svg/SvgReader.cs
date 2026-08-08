using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace FileFormat.Svg;

/// <summary>Parses an SVG drawing's XML.</summary>
public static class SvgReader {

  public static SvgFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SVG drawing not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SvgFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static SvgFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static SvgFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException("An SVG drawing is an XML document and this is too short to be one.");

    XDocument document;
    using (var memory = new MemoryStream(data.ToArray())) {
      var settings = new XmlReaderSettings {
        // Several of these carry their styles as entities declared in an internal subset, so the
        // subset has to be read. The resolver is left null so nothing outside the file is fetched:
        // a drawing is a picture, and a picture has no business opening a network connection.
        DtdProcessing = DtdProcessing.Parse,
        XmlResolver = null,
        MaxCharactersFromEntities = 1 << 24,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false
      };

      try {
        using var reader = XmlReader.Create(memory, settings);
        document = XDocument.Load(reader);
      } catch (XmlException failure) {
        throw new InvalidDataException($"Not an SVG drawing: {failure.Message}", failure);
      }
    }

    var root = document.Root;
    if (root == null || root.Name.LocalName != "svg")
      throw new InvalidDataException($"An SVG drawing has an svg root and this one has {(root == null ? "none" : root.Name.LocalName)}.");

    // The namespace is what says this is the W3C's format rather than somebody else's element of
    // the same name; a document with no namespace at all is accepted because plenty of real files
    // leave it off, but one that names a different format is not.
    var space = root.Name.NamespaceName;
    if (space.Length > 0 && space != SvgFile.Namespace)
      throw new InvalidDataException($"An svg root in the namespace {space} is not a Scalable Vector Graphics drawing.");

    return new() { Document = document };
  }
}
