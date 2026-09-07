using System;
using System.IO;
using FileFormat.Core;
using FileFormat.OfficeOpenXml;

namespace FileFormat.PowerPoint;

/// <summary>The picture inside a PowerPoint presentation, slide show, or template.</summary>
/// <remarks>
/// Legacy <c>.ppt</c>, <c>.pps</c> and <c>.pot</c> files use Microsoft Compound File Binary and
/// OfficeArt BLIP records. The compatibility reader intentionally keeps the historical XnView-style
/// raw OfficeArt walk used by this image format. Its legacy writer likewise emits the image-level
/// CFB <c>Pictures</c> carrier rather than claiming to implement the whole binary presentation model.
/// <para/>
/// Modern <c>.pptx</c>/<c>.ppsx</c>/<c>.potx</c> and macro-capable
/// <c>.pptm</c>/<c>.ppsm</c>/<c>.potm</c> are native PresentationML packages. Those variants are
/// written as complete minimum presentations: presentation properties, slide master, blank layout,
/// theme, one slide, relationships, and the supplied picture as a PNG on that slide. Macro-capable
/// output uses the correct main-part content type but contains no fabricated VBA project.
/// </remarks>
public readonly record struct PowerPointFile
  : IImageFormatReader<PowerPointFile>, IImageToRawImage<PowerPointFile>,
    IImageFromRawImage<PowerPointFile>, IImageFormatWriter<PowerPointFile> {

  /// <summary>The eight bytes a Microsoft compound document opens with.</summary>
  public static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

  /// <summary>Where the legacy OfficeArt record walk begins, behind the CFB header.</summary>
  public const int ScanStart = 512;

  /// <summary>Version, instance, type and length.</summary>
  public const int RecordHeaderSize = 8;

  /// <summary>A checksum and a tag byte stand between a BLIP's header and the picture in it.</summary>
  public const int BlipPrefixSize = 17;

  public const ushort JpegBlipType = 0xF01D;
  public const ushort JpegBlipVersionAndInstance = 0x46A0;
  public const ushort PngBlipType = 0xF01E;
  public const ushort PngBlipVersionAndInstance = 0x6E00;

  static string IImageFormatMetadata<PowerPointFile>.PrimaryExtension => ".ppt";
  static string[] IImageFormatMetadata<PowerPointFile>.FileExtensions => [
    ".ppt", ".pps", ".pot",
    ".pptx", ".ppsx", ".potx",
    ".pptm", ".ppsm", ".potm",
  ];
  static PowerPointFile IImageFormatReader<PowerPointFile>.FromSpan(ReadOnlySpan<byte> data)
    => PowerPointReader.FromSpan(data);
  static PowerPointFile IImageFromRawImage<PowerPointFile>.FromRawImage(RawImage image, string extension)
    => FromRawImage(image, extension);
  static byte[] IImageFormatWriter<PowerPointFile>.ToBytes(PowerPointFile file)
    => PowerPointWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<PowerPointFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>CFB and ZIP are both shared containers, so either signature only means “possible”.</summary>
  static bool? IImageFormatMetadata<PowerPointFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
      return null;
    if (header.Length < Signature.Length)
      return null;
    return header[..Signature.Length].SequenceEqual(Signature) ? null : false;
  }

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  internal PowerPointKind Kind { get; init; }

  public static RawImage ToRawImage(PowerPointFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };

  /// <summary>Creates the historical .ppt image carrier when no target extension is supplied.</summary>
  public static PowerPointFile FromRawImage(RawImage image) => FromRawImage(image, ".ppt");

  /// <summary>Creates the PowerPoint variant selected by its target extension.</summary>
  public static PowerPointFile FromRawImage(RawImage image, string extension) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentOutOfRangeException(nameof(image), $"A PowerPoint picture needs positive dimensions, not {image.Width}x{image.Height}.");
    if (!image.HasEnoughPixelData)
      throw new InvalidDataException($"The {image.Width}x{image.Height} {image.Format} source does not contain enough pixel data.");

    var converted = image.EnsureFormat(PixelFormat.Rgb24);
    var pixelLength = checked(converted.Width * converted.Height * 3);
    return new() {
      Width = converted.Width,
      Height = converted.Height,
      PixelData = converted.PixelData[..pixelLength],
      Kind = PowerPointKindExtensions.FromExtension(extension),
    };
  }
}

internal enum PowerPointKind {
  Legacy,
  Presentation,
  SlideShow,
  Template,
  MacroPresentation,
  MacroSlideShow,
  MacroTemplate,
}

internal static class PowerPointKindExtensions {
  internal static PowerPointKind FromExtension(string extension) {
    ArgumentException.ThrowIfNullOrWhiteSpace(extension);
    return extension.ToLowerInvariant() switch {
      ".ppt" or ".pps" or ".pot" => PowerPointKind.Legacy,
      ".pptx" => PowerPointKind.Presentation,
      ".ppsx" => PowerPointKind.SlideShow,
      ".potx" => PowerPointKind.Template,
      ".pptm" => PowerPointKind.MacroPresentation,
      ".ppsm" => PowerPointKind.MacroSlideShow,
      ".potm" => PowerPointKind.MacroTemplate,
      _ => throw new ArgumentException($"Unsupported PowerPoint extension '{extension}'.", nameof(extension)),
    };
  }

  internal static PowerPointKind FromContentType(string contentType) => contentType switch {
    OfficeOpenXmlImagePackage.PowerPointPresentationContentType => PowerPointKind.Presentation,
    OfficeOpenXmlImagePackage.PowerPointSlideShowContentType => PowerPointKind.SlideShow,
    OfficeOpenXmlImagePackage.PowerPointTemplateContentType => PowerPointKind.Template,
    OfficeOpenXmlImagePackage.PowerPointMacroPresentationContentType => PowerPointKind.MacroPresentation,
    OfficeOpenXmlImagePackage.PowerPointMacroSlideShowContentType => PowerPointKind.MacroSlideShow,
    OfficeOpenXmlImagePackage.PowerPointMacroTemplateContentType => PowerPointKind.MacroTemplate,
    _ => throw new InvalidDataException($"Unsupported PowerPoint main-part content type '{contentType}'."),
  };

  internal static bool IsOpenXml(this PowerPointKind kind) => kind != PowerPointKind.Legacy;

  internal static string ContentType(this PowerPointKind kind) => kind switch {
    PowerPointKind.Presentation => OfficeOpenXmlImagePackage.PowerPointPresentationContentType,
    PowerPointKind.SlideShow => OfficeOpenXmlImagePackage.PowerPointSlideShowContentType,
    PowerPointKind.Template => OfficeOpenXmlImagePackage.PowerPointTemplateContentType,
    PowerPointKind.MacroPresentation => OfficeOpenXmlImagePackage.PowerPointMacroPresentationContentType,
    PowerPointKind.MacroSlideShow => OfficeOpenXmlImagePackage.PowerPointMacroSlideShowContentType,
    PowerPointKind.MacroTemplate => OfficeOpenXmlImagePackage.PowerPointMacroTemplateContentType,
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Legacy PowerPoint has no OOXML main-part content type."),
  };
}
