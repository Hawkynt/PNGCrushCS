using System;
using System.IO;
using FileFormat.Core;
using FileFormat.OfficeOpenXml;

namespace FileFormat.Excel;

/// <summary>The picture carried by an Excel Open XML workbook or template.</summary>
/// <remarks>
/// Reading returns the first raster stored below <c>xl/media</c>. Writing creates a native,
/// minimal SpreadsheetML workbook with one worksheet and one drawing anchored at cell A1. The
/// extension selects workbook versus template and ordinary versus macro-capable main-part content
/// types; no VBA project is invented for macro-capable output when the source contains only pixels.
/// </remarks>
public readonly record struct ExcelFile
  : IImageFormatReader<ExcelFile>, IImageToRawImage<ExcelFile>,
    IImageFromRawImage<ExcelFile>, IImageFormatWriter<ExcelFile> {

  static string IImageFormatMetadata<ExcelFile>.PrimaryExtension => ".xlsx";
  static string[] IImageFormatMetadata<ExcelFile>.FileExtensions => [".xlsx", ".xlsm", ".xltx", ".xltm"];
  static ExcelFile IImageFormatReader<ExcelFile>.FromSpan(ReadOnlySpan<byte> data) => ExcelReader.FromSpan(data);
  static ExcelFile IImageFromRawImage<ExcelFile>.FromRawImage(RawImage image, string extension) => FromRawImage(image, extension);
  static byte[] IImageFormatWriter<ExcelFile>.ToBytes(ExcelFile file) => ExcelWriter.ToBytes(file);

  static bool? IImageFormatMetadata<ExcelFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;
    return header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04 ? null : false;
  }

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  internal ExcelOpenXmlKind Kind { get; init; }

  public static RawImage ToRawImage(ExcelFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };

  public static ExcelFile FromRawImage(RawImage image) => FromRawImage(image, ".xlsx");

  /// <summary>Creates the Excel variant named by .xlsx, .xlsm, .xltx, or .xltm.</summary>
  public static ExcelFile FromRawImage(RawImage image, string extension) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(image), $"An Excel picture needs positive dimensions, not {image.Width}x{image.Height}.");
    if (!image.HasEnoughPixelData)
      throw new InvalidDataException($"The {image.Width}x{image.Height} {image.Format} source does not contain enough pixel data.");

    var converted = image.EnsureFormat(PixelFormat.Rgb24);
    var pixelLength = checked(converted.Width * converted.Height * 3);
    return new() {
      Width = converted.Width,
      Height = converted.Height,
      PixelData = converted.PixelData[..pixelLength],
      Kind = ExcelOpenXmlKindExtensions.FromExtension(extension),
    };
  }
}

internal enum ExcelOpenXmlKind {
  Workbook,
  MacroWorkbook,
  Template,
  MacroTemplate,
}

internal static class ExcelOpenXmlKindExtensions {
  internal static ExcelOpenXmlKind FromExtension(string extension) {
    ArgumentException.ThrowIfNullOrWhiteSpace(extension);
    return extension.ToLowerInvariant() switch {
      ".xlsx" => ExcelOpenXmlKind.Workbook,
      ".xlsm" => ExcelOpenXmlKind.MacroWorkbook,
      ".xltx" => ExcelOpenXmlKind.Template,
      ".xltm" => ExcelOpenXmlKind.MacroTemplate,
      _ => throw new ArgumentException($"Unsupported Excel Open XML extension '{extension}'.", nameof(extension)),
    };
  }

  internal static ExcelOpenXmlKind FromContentType(string contentType) => contentType switch {
    OfficeOpenXmlImagePackage.ExcelWorkbookContentType => ExcelOpenXmlKind.Workbook,
    OfficeOpenXmlImagePackage.ExcelMacroWorkbookContentType => ExcelOpenXmlKind.MacroWorkbook,
    OfficeOpenXmlImagePackage.ExcelTemplateContentType => ExcelOpenXmlKind.Template,
    OfficeOpenXmlImagePackage.ExcelMacroTemplateContentType => ExcelOpenXmlKind.MacroTemplate,
    _ => throw new InvalidDataException($"Unsupported Excel main-part content type '{contentType}'."),
  };

  internal static string ContentType(this ExcelOpenXmlKind kind) => kind switch {
    ExcelOpenXmlKind.Workbook => OfficeOpenXmlImagePackage.ExcelWorkbookContentType,
    ExcelOpenXmlKind.MacroWorkbook => OfficeOpenXmlImagePackage.ExcelMacroWorkbookContentType,
    ExcelOpenXmlKind.Template => OfficeOpenXmlImagePackage.ExcelTemplateContentType,
    ExcelOpenXmlKind.MacroTemplate => OfficeOpenXmlImagePackage.ExcelMacroTemplateContentType,
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };
}

public static class ExcelReader {
  public static ExcelFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4 || data[0] != 0x50 || data[1] != 0x4B || data[2] != 0x03 || data[3] != 0x04)
      throw new InvalidDataException("Not an Excel Open XML workbook: ZIP/OPC signature is missing.");

    var (image, contentType) = OfficeOpenXmlImagePackage.ReadFirstImage(data, "xl/media/", "/xl/workbook.xml");
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData,
      Kind = ExcelOpenXmlKindExtensions.FromContentType(contentType),
    };
  }
}

public static class ExcelWriter {
  public static byte[] ToBytes(ExcelFile file) {
    if (file.PixelData is null)
      throw new InvalidDataException("Excel pixel data is missing.");
    return OfficeOpenXmlImagePackage.WriteExcel(file.Width, file.Height, file.PixelData, file.Kind.ContentType());
  }
}
