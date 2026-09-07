using System;
using System.IO;
using FileFormat.Core;
using FileFormat.OfficeOpenXml;

namespace FileFormat.Word;

/// <summary>The picture carried by a Word Open XML document or template.</summary>
/// <remarks>
/// The image library models the first raster under <c>word/media</c>. Writing creates a native,
/// minimal WordprocessingML package whose body contains that picture as an inline drawing. The
/// extension selects document versus template and ordinary versus macro-capable main-part content
/// types; macro-capable output intentionally contains no VBA project when the source is only pixels.
/// </remarks>
public readonly record struct WordFile
  : IImageFormatReader<WordFile>, IImageToRawImage<WordFile>,
    IImageFromRawImage<WordFile>, IImageFormatWriter<WordFile> {

  static string IImageFormatMetadata<WordFile>.PrimaryExtension => ".docx";
  static string[] IImageFormatMetadata<WordFile>.FileExtensions => [".docx", ".docm", ".dotx", ".dotm"];
  static WordFile IImageFormatReader<WordFile>.FromSpan(ReadOnlySpan<byte> data) => WordReader.FromSpan(data);
  static WordFile IImageFromRawImage<WordFile>.FromRawImage(RawImage image, string extension) => FromRawImage(image, extension);
  static byte[] IImageFormatWriter<WordFile>.ToBytes(WordFile file) => WordWriter.ToBytes(file);

  static bool? IImageFormatMetadata<WordFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;
    return header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04 ? null : false;
  }

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  internal WordOpenXmlKind Kind { get; init; }

  public static RawImage ToRawImage(WordFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };

  public static WordFile FromRawImage(RawImage image) => FromRawImage(image, ".docx");

  /// <summary>Creates the Word variant named by .docx, .docm, .dotx, or .dotm.</summary>
  public static WordFile FromRawImage(RawImage image, string extension) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(image), $"A Word picture needs positive dimensions, not {image.Width}x{image.Height}.");
    if (!image.HasEnoughPixelData)
      throw new InvalidDataException($"The {image.Width}x{image.Height} {image.Format} source does not contain enough pixel data.");

    var converted = image.EnsureFormat(PixelFormat.Rgb24);
    var pixelLength = checked(converted.Width * converted.Height * 3);
    return new() {
      Width = converted.Width,
      Height = converted.Height,
      PixelData = converted.PixelData[..pixelLength],
      Kind = WordOpenXmlKind.FromExtension(extension),
    };
  }
}

internal enum WordOpenXmlKind {
  Document,
  MacroDocument,
  Template,
  MacroTemplate,
}

internal static class WordOpenXmlKindExtensions {
  internal static WordOpenXmlKind FromExtension(string extension) {
    ArgumentException.ThrowIfNullOrWhiteSpace(extension);
    return extension.ToLowerInvariant() switch {
      ".docx" => WordOpenXmlKind.Document,
      ".docm" => WordOpenXmlKind.MacroDocument,
      ".dotx" => WordOpenXmlKind.Template,
      ".dotm" => WordOpenXmlKind.MacroTemplate,
      _ => throw new ArgumentException($"Unsupported Word Open XML extension '{extension}'.", nameof(extension)),
    };
  }

  internal static WordOpenXmlKind FromContentType(string contentType) => contentType switch {
    OfficeOpenXmlImagePackage.WordDocumentContentType => WordOpenXmlKind.Document,
    OfficeOpenXmlImagePackage.WordMacroDocumentContentType => WordOpenXmlKind.MacroDocument,
    OfficeOpenXmlImagePackage.WordTemplateContentType => WordOpenXmlKind.Template,
    OfficeOpenXmlImagePackage.WordMacroTemplateContentType => WordOpenXmlKind.MacroTemplate,
    _ => throw new InvalidDataException($"Unsupported Word main-part content type '{contentType}'."),
  };

  internal static string ContentType(this WordOpenXmlKind kind) => kind switch {
    WordOpenXmlKind.Document => OfficeOpenXmlImagePackage.WordDocumentContentType,
    WordOpenXmlKind.MacroDocument => OfficeOpenXmlImagePackage.WordMacroDocumentContentType,
    WordOpenXmlKind.Template => OfficeOpenXmlImagePackage.WordTemplateContentType,
    WordOpenXmlKind.MacroTemplate => OfficeOpenXmlImagePackage.WordMacroTemplateContentType,
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };
}

public static class WordReader {
  public static WordFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4 || data[0] != 0x50 || data[1] != 0x4B || data[2] != 0x03 || data[3] != 0x04)
      throw new InvalidDataException("Not a Word Open XML document: ZIP/OPC signature is missing.");

    var (image, contentType) = OfficeOpenXmlImagePackage.ReadFirstImage(data, "word/media/", "/word/document.xml");
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData,
      Kind = WordOpenXmlKindExtensions.FromContentType(contentType),
    };
  }
}

public static class WordWriter {
  public static byte[] ToBytes(WordFile file) {
    if (file.PixelData is null)
      throw new InvalidDataException("Word pixel data is missing.");
    return OfficeOpenXmlImagePackage.WriteWord(file.Width, file.Height, file.PixelData, file.Kind.ContentType());
  }
}
