using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.OfficeOpenXml;

namespace FileFormat.Word;

/// <summary>The images carried by a Word Open XML document or template.</summary>
/// <remarks>
/// Reading enumerates every decodable image part in the OPC package, not just the first item under
/// <c>word/media</c>. That includes ordinary document/background pictures and any image-typed package
/// thumbnail, icon or object/control preview a producer stores elsewhere. The ordinary
/// <see cref="ToRawImage(WordFile)"/> API remains first-image compatibility; multi-image callers can
/// enumerate them all through <see cref="IMultiImageFileFormat{TSelf}"/>.
/// <para/>
/// Writing creates a native minimal WordprocessingML package whose body contains the source picture
/// as an inline drawing. The extension selects document versus template and ordinary versus
/// macro-capable main-part content types; macro-capable output intentionally contains no VBA project
/// when the source is only pixels.
/// </remarks>
public readonly record struct WordFile
  : IImageFormatReader<WordFile>, IImageToRawImage<WordFile>,
    IImageFromRawImage<WordFile>, IImageFormatWriter<WordFile>, IMultiImageFileFormat<WordFile> {

  static string IImageFormatMetadata<WordFile>.PrimaryExtension => ".docx";
  static string[] IImageFormatMetadata<WordFile>.FileExtensions => [".docx", ".docm", ".dotx", ".dotm"];
  static FormatCapability IImageFormatMetadata<WordFile>.Capabilities => FormatCapability.MultiImage;
  static WordFile IImageFormatReader<WordFile>.FromSpan(ReadOnlySpan<byte> data) => WordReader.FromSpan(data);
  static WordFile IImageFromRawImage<WordFile>.FromRawImage(RawImage image, string extension) => FromRawImage(image, extension);
  static byte[] IImageFormatWriter<WordFile>.ToBytes(WordFile file) => WordWriter.ToBytes(file);

  static bool? IImageFormatMetadata<WordFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;
    return header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04 ? null : false;
  }

  /// <summary>Compatibility dimensions of the first image.</summary>
  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  /// <summary>Every decodable image carried by the package, in package order with Word media first.</summary>
  public IReadOnlyList<RawImage> Images { get; init; } = [];

  internal WordOpenXmlKind Kind { get; init; }

  public static int ImageCount(WordFile file)
    => file.Images.Count > 0 ? file.Images.Count : _HasCompatibilityImage(file) ? 1 : 0;

  public static RawImage ToRawImage(WordFile file, int index) {
    var count = ImageCount(file);
    if ((uint)index >= (uint)count)
      throw new ArgumentOutOfRangeException(nameof(index));
    if (file.Images.Count > 0)
      return file.Images[index];

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..checked(file.Width * file.Height * 3)],
    };
  }

  public static RawImage ToRawImage(WordFile file) {
    if (ImageCount(file) == 0)
      throw new InvalidDataException("The Word document contains no decodable images.");
    return ToRawImage(file, 0);
  }

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
    var stored = new RawImage {
      Width = converted.Width,
      Height = converted.Height,
      Format = PixelFormat.Rgb24,
      PixelData = converted.PixelData[..pixelLength],
    };
    return new() {
      Width = stored.Width,
      Height = stored.Height,
      PixelData = stored.PixelData[..],
      Images = [stored],
      Kind = WordOpenXmlKindExtensions.FromExtension(extension),
    };
  }

  private static bool _HasCompatibilityImage(WordFile file) {
    if (file.Width <= 0 || file.Height <= 0 || file.PixelData is null)
      return false;
    try {
      return file.PixelData.Length >= checked(file.Width * file.Height * 3);
    } catch (OverflowException) {
      return false;
    }
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

    var result = OfficeOpenXmlImageReader.ReadAll(data, "word/media/", "/word/document.xml");
    var first = result.Images.Count > 0 ? result.Images[0].EnsureFormat(PixelFormat.Rgb24) : null;
    return new() {
      Width = first?.Width ?? 0,
      Height = first?.Height ?? 0,
      PixelData = first?.PixelData[..] ?? [],
      Images = result.Images,
      Kind = WordOpenXmlKindExtensions.FromContentType(result.MainContentType),
    };
  }
}

public static class WordWriter {
  public static byte[] ToBytes(WordFile file) {
    var image = WordFile.ImageCount(file) > 0
      ? WordFile.ToRawImage(file, 0).EnsureFormat(PixelFormat.Rgb24)
      : throw new InvalidDataException("Word document contains no picture to write.");
    return OfficeOpenXmlImagePackage.WriteWord(image.Width, image.Height, image.PixelData, file.Kind.ContentType());
  }
}
