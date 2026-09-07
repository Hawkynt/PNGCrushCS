using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FileFormat.Core;
using FileFormat.Emf;
using FileFormat.EmbeddedPicture;
using FileFormat.Gif;
using FileFormat.Ico;
using FileFormat.Pcx;
using FileFormat.Svg;
using FileFormat.Tiff;
using FileFormat.WebP;

namespace FileFormat.OfficeOpenXml;

/// <summary>Finds every decodable image part carried by an Office Open XML package.</summary>
/// <remarks>
/// Images in Office are not limited to the application's conventional <c>word/media</c>,
/// <c>xl/media</c>, or <c>ppt/media</c> folder. Package thumbnails, control/object previews,
/// custom-UI icons and other producer-specific image parts can live elsewhere. OPC already tells us
/// what each part is through <c>[Content_Types].xml</c>, so this walks every part whose resolved
/// content type is <c>image/*</c>. The application's media folder is only used as an ordering hint so
/// the historical single-image API continues to return a document picture before a package thumbnail.
/// <para/>
/// Multi-image image parts are flattened: every ICO entry, TIFF page, GIF frame and WebP frame is an
/// image of the Office file. Vector formats are decoded when this repository already has a managed
/// renderer. A malformed or unsupported individual image part is skipped without making an otherwise
/// valid document unreadable.
/// </remarks>
internal static class OfficeOpenXmlImageReader {

  private const string _ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

  internal readonly record struct Result(IReadOnlyList<RawImage> Images, string MainContentType);

  internal static Result ReadAll(
    ReadOnlySpan<byte> data,
    string preferredMediaPrefix,
    string mainPartName) {

    try {
      using var memory = new MemoryStream(data.ToArray(), false);
      using var archive = new ZipArchive(memory, ZipArchiveMode.Read, false, Encoding.UTF8);
      var (defaults, overrides) = _ReadContentTypes(archive);
      var mainContentType = overrides.TryGetValue(mainPartName, out var declared)
        ? declared
        : throw new InvalidDataException($"The Office package does not declare the main part {mainPartName}.");

      var candidates = archive.Entries
        .Select((entry, index) => (Entry: entry, Index: index, ContentType: _ContentType(entry.FullName, defaults, overrides)))
        .Where(candidate => candidate.Entry.Length > 0
          && !candidate.Entry.FullName.EndsWith("/", StringComparison.Ordinal)
          && candidate.ContentType is not null
          && candidate.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        .OrderBy(candidate => candidate.Entry.FullName.StartsWith(preferredMediaPrefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .ThenBy(candidate => candidate.Index)
        .ToArray();

      var result = new List<RawImage>();
      foreach (var candidate in candidates) {
        if (candidate.Entry.Length > int.MaxValue)
          continue;

        using var stream = candidate.Entry.Open();
        using var image = new MemoryStream(checked((int)candidate.Entry.Length));
        stream.CopyTo(image);
        var bytes = image.ToArray();

        try {
          _DecodeAll(bytes, candidate.ContentType!, result);
        } catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException) {
          // One bad/unsupported preview must not hide the other images a valid Office package carries.
        }
      }

      return new(result.ToArray(), mainContentType);
    } catch (InvalidDataException) {
      throw;
    } catch (Exception exception) when (exception is IOException or NotSupportedException or ArgumentException) {
      throw new InvalidDataException("The Office Open XML package is malformed.", exception);
    }
  }

  private static (Dictionary<string, string> Defaults, Dictionary<string, string> Overrides) _ReadContentTypes(ZipArchive archive) {
    var entry = archive.GetEntry("[Content_Types].xml")
      ?? throw new InvalidDataException("The Office Open XML package has no [Content_Types].xml part.");

    using var stream = entry.Open();
    var document = XDocument.Load(stream, LoadOptions.None);
    var ns = XNamespace.Get(_ContentTypesNamespace);
    var root = document.Root ?? throw new InvalidDataException("The Office content-type manifest is empty.");

    var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var element in root.Elements(ns + "Default")) {
      var extension = (string?)element.Attribute("Extension");
      var contentType = (string?)element.Attribute("ContentType");
      if (!string.IsNullOrWhiteSpace(extension) && !string.IsNullOrWhiteSpace(contentType))
        defaults[extension.TrimStart('.')] = contentType;
    }

    var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var element in root.Elements(ns + "Override")) {
      var partName = (string?)element.Attribute("PartName");
      var contentType = (string?)element.Attribute("ContentType");
      if (!string.IsNullOrWhiteSpace(partName) && !string.IsNullOrWhiteSpace(contentType))
        overrides[_PartName(partName)] = contentType;
    }

    return (defaults, overrides);
  }

  private static string? _ContentType(
    string path,
    IReadOnlyDictionary<string, string> defaults,
    IReadOnlyDictionary<string, string> overrides) {

    if (overrides.TryGetValue(_PartName(path), out var overridden))
      return overridden;

    var extension = Path.GetExtension(path);
    return extension.Length > 1 && defaults.TryGetValue(extension[1..], out var defaulted)
      ? defaulted
      : null;
  }

  private static string _PartName(string path)
    => path.StartsWith('/', StringComparison.Ordinal) ? path : "/" + path;

  private static void _DecodeAll(byte[] bytes, string contentType, List<RawImage> output) {
    if (_IsGif(bytes)) {
      var file = GifReader.FromSpan(bytes);
      for (var i = 0; i < GifFile.ImageCount(file); ++i)
        output.Add(GifFile.ToRawImage(file, i));
      return;
    }

    if (_IsTiff(bytes)) {
      var file = TiffReader.FromSpan(bytes);
      for (var i = 0; i < TiffFile.ImageCount(file); ++i)
        output.Add(TiffFile.ToRawImage(file, i));
      return;
    }

    if (_IsIco(bytes)) {
      var file = IcoReader.FromSpan(bytes);
      for (var i = 0; i < IcoFile.ImageCount(file); ++i)
        output.Add(IcoFile.ToRawImage(file, i));
      return;
    }

    if (_IsWebP(bytes)) {
      var file = WebPReader.FromSpan(bytes);
      for (var i = 0; i < WebPFile.ImageCount(file); ++i)
        output.Add(WebPFile.ToRawImage(file, i));
      return;
    }

    if (_IsEmf(bytes)) {
      output.Add(EmfFile.ToRawImage(EmfReader.FromSpan(bytes)));
      return;
    }

    if (contentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase)) {
      output.Add(SvgFile.ToRawImage(SvgReader.FromSpan(bytes)));
      return;
    }

    if (contentType.Equals("image/x-pcx", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("image/pcx", StringComparison.OrdinalIgnoreCase)) {
      output.Add(PcxFile.ToRawImage(PcxReader.FromSpan(bytes)));
      return;
    }

    output.Add(EmbeddedPictureReader.Decode(bytes));
  }

  private static bool _IsGif(ReadOnlySpan<byte> data)
    => data.Length >= 6 && data[..3].SequenceEqual("GIF"u8);

  private static bool _IsTiff(ReadOnlySpan<byte> data)
    => data.Length >= 4
      && (data[..4].SequenceEqual([0x49, 0x49, 0x2A, 0x00])
        || data[..4].SequenceEqual([0x4D, 0x4D, 0x00, 0x2A]));

  private static bool _IsIco(ReadOnlySpan<byte> data)
    => data.Length >= 6 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0;

  private static bool _IsWebP(ReadOnlySpan<byte> data)
    => data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8);

  private static bool _IsEmf(ReadOnlySpan<byte> data)
    => data.Length >= 44
      && data[0] == 1 && data[1] == 0 && data[2] == 0 && data[3] == 0
      && data.Slice(40, 4).SequenceEqual([0x20, 0x45, 0x4D, 0x46]);
}
