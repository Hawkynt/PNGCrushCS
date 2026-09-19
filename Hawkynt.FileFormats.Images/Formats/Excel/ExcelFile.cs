using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Fpx;
using FileFormat.OfficeOpenXml;

namespace FileFormat.Excel;

/// <summary>The images carried by an Excel workbook or template.</summary>
/// <remarks>
/// Open XML reading enumerates every decodable image part in the OPC package, not only
/// <c>xl/media</c>. Worksheet/background pictures therefore sit alongside image-typed package
/// thumbnails, custom UI icons and object/control previews stored elsewhere. Legacy BIFF8
/// <c>.xls</c>/<c>.xlt</c> reading extracts the worksheet background image carried by the BkHim
/// record. The ordinary <see cref="ToRawImage(ExcelFile)"/> API remains first-image compatibility;
/// multi-image callers can enumerate every extracted asset.
/// <para/>
/// Open XML writing creates a native minimal SpreadsheetML workbook with one worksheet and one
/// drawing anchored at cell A1. Legacy writing creates a native BIFF8 Workbook stream in a Compound
/// File Binary container and stores the source picture as the worksheet BkHim background. The
/// extension selects workbook versus template and ordinary versus macro-capable Open XML content;
/// no VBA project is invented for macro-capable output when the source contains only pixels.
/// </remarks>
public readonly record struct ExcelFile()
  : IImageFormatReader<ExcelFile>, IImageToRawImage<ExcelFile>,
    IImageFromRawImage<ExcelFile>, IImageFormatWriter<ExcelFile>, IMultiImageFileFormat<ExcelFile> {

  static string IImageFormatMetadata<ExcelFile>.PrimaryExtension => ".xlsx";
  static string[] IImageFormatMetadata<ExcelFile>.FileExtensions => [".xls", ".xlt", ".xlsx", ".xlsm", ".xltx", ".xltm"];
  static FormatCapability IImageFormatMetadata<ExcelFile>.Capabilities => FormatCapability.MultiImage;
  static ExcelFile IImageFormatReader<ExcelFile>.FromSpan(ReadOnlySpan<byte> data) => ExcelReader.FromSpan(data);
  static ExcelFile IImageFromRawImage<ExcelFile>.FromRawImage(RawImage image, string extension) => FromRawImage(image, extension);
  static byte[] IImageFormatWriter<ExcelFile>.ToBytes(ExcelFile file) => ExcelWriter.ToBytes(file);

  static bool? IImageFormatMetadata<ExcelFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 4)
      return null;
    if (header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
      return null;
    if (CompoundFile.HasSignature(header))
      return null;
    return false;
  }

  /// <summary>Compatibility dimensions of the first image.</summary>
  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  /// <summary>Every decodable image carried by the package, in package order with Excel media first.</summary>
  public IReadOnlyList<RawImage> Images { get; init; } = [];

  internal ExcelOpenXmlKind Kind { get; init; }

  public static int ImageCount(ExcelFile file)
    => file.Images is { Count: > 0 } images ? images.Count : _HasCompatibilityImage(file) ? 1 : 0;

  public static RawImage ToRawImage(ExcelFile file, int index) {
    var count = ImageCount(file);
    if ((uint)index >= (uint)count)
      throw new ArgumentOutOfRangeException(nameof(index));
    if (file.Images is { Count: > 0 } images)
      return images[index];

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..checked(file.Width * file.Height * 3)],
    };
  }

  public static RawImage ToRawImage(ExcelFile file) {
    if (ImageCount(file) == 0)
      throw new InvalidDataException("The Excel workbook contains no decodable images.");
    return ToRawImage(file, 0);
  }

  public static ExcelFile FromRawImage(RawImage image) => FromRawImage(image, ".xlsx");

  /// <summary>Creates the Excel variant named by .xls, .xlt, .xlsx, .xlsm, .xltx, or .xltm.</summary>
  public static ExcelFile FromRawImage(RawImage image, string extension) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(image), $"An Excel picture needs positive dimensions, not {image.Width}x{image.Height}.");
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
      Kind = ExcelOpenXmlKindExtensions.FromExtension(extension),
    };
  }

  private static bool _HasCompatibilityImage(ExcelFile file) {
    if (file.Width <= 0 || file.Height <= 0 || file.PixelData is null)
      return false;
    try {
      return file.PixelData.Length >= checked(file.Width * file.Height * 3);
    } catch (OverflowException) {
      return false;
    }
  }
}

internal enum ExcelOpenXmlKind {
  LegacyWorkbook,
  LegacyTemplate,
  Workbook,
  MacroWorkbook,
  Template,
  MacroTemplate,
}

internal static class ExcelOpenXmlKindExtensions {
  internal static ExcelOpenXmlKind FromExtension(string extension) {
    ArgumentException.ThrowIfNullOrWhiteSpace(extension);
    return extension.ToLowerInvariant() switch {
      ".xls" => ExcelOpenXmlKind.LegacyWorkbook,
      ".xlt" => ExcelOpenXmlKind.LegacyTemplate,
      ".xlsx" => ExcelOpenXmlKind.Workbook,
      ".xlsm" => ExcelOpenXmlKind.MacroWorkbook,
      ".xltx" => ExcelOpenXmlKind.Template,
      ".xltm" => ExcelOpenXmlKind.MacroTemplate,
      _ => throw new ArgumentException($"Unsupported Excel extension '{extension}'.", nameof(extension)),
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
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Legacy BIFF8 workbooks do not have an OPC content type."),
  };

  internal static bool IsLegacy(this ExcelOpenXmlKind kind)
    => kind is ExcelOpenXmlKind.LegacyWorkbook or ExcelOpenXmlKind.LegacyTemplate;
}

public static class ExcelReader {
  public static ExcelFile FromSpan(ReadOnlySpan<byte> data) {
    if (CompoundFile.HasSignature(data))
      return ExcelBinaryFile.Read(data);

    if (data.Length < 4 || data[0] != 0x50 || data[1] != 0x4B || data[2] != 0x03 || data[3] != 0x04)
      throw new InvalidDataException("Not an Excel workbook: neither CFB/BIFF nor ZIP/OPC signature is present.");

    var result = OfficeOpenXmlImageReader.ReadAll(data, "xl/media/", "/xl/workbook.xml");
    var first = result.Images.Count > 0 ? result.Images[0].EnsureFormat(PixelFormat.Rgb24) : null;
    return new() {
      Width = first?.Width ?? 0,
      Height = first?.Height ?? 0,
      PixelData = first?.PixelData[..] ?? [],
      Images = result.Images,
      Kind = ExcelOpenXmlKindExtensions.FromContentType(result.MainContentType),
    };
  }
}

public static class ExcelWriter {
  public static byte[] ToBytes(ExcelFile file) {
    var image = ExcelFile.ImageCount(file) > 0
      ? ExcelFile.ToRawImage(file, 0).EnsureFormat(PixelFormat.Rgb24)
      : throw new InvalidDataException("Excel workbook contains no picture to write.");

    return file.Kind.IsLegacy()
      ? ExcelBinaryFile.Write(image)
      : OfficeOpenXmlImagePackage.WriteExcel(image.Width, image.Height, image.PixelData, file.Kind.ContentType());
  }
}
