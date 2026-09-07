using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.OfficeOpenXml;

namespace FileFormat.PowerPoint;

/// <summary>The images inside a PowerPoint presentation, slide show, or template.</summary>
/// <remarks>
/// Legacy <c>.ppt</c>, <c>.pps</c> and <c>.pot</c> files use Microsoft Compound File Binary and
/// OfficeArt BLIP records. Reading exposes every supported BLIP from the Pictures stream, while the
/// compatibility <see cref="ToRawImage(PowerPointFile)"/> call returns the first one. Its legacy
/// writer emits the image-level CFB <c>Pictures</c> carrier rather than claiming to implement the
/// whole binary presentation model.
/// <para/>
/// Modern <c>.pptx</c>/<c>.ppsx</c>/<c>.potx</c> and macro-capable
/// <c>.pptm</c>/<c>.ppsm</c>/<c>.potm</c> are native PresentationML packages. Reading enumerates all
/// decodable image parts, including slide/master/background media and package thumbnails/icons/object
/// previews stored outside <c>ppt/media</c>. Writing creates a complete minimum presentation with one
/// picture-only slide. Macro-capable output uses the correct main-part content type but contains no
/// fabricated VBA project.
/// </remarks>
public readonly record struct PowerPointFile
  : IImageFormatReader<PowerPointFile>, IImageToRawImage<PowerPointFile>,
    IImageFromRawImage<PowerPointFile>, IImageFormatWriter<PowerPointFile>,
    IMultiImageFileFormat<PowerPointFile> {

  /// <summary>The eight bytes a Microsoft compound document opens with.</summary>
  public static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

  /// <summary>Where the historical raw OfficeArt fallback begins, behind the CFB header.</summary>
  public const int ScanStart = 512;

  /// <summary>Version, instance, type and length.</summary>
  public const int RecordHeaderSize = 8;

  /// <summary>A one-UID raster BLIP has a sixteen-byte UID and one tag byte before its image.</summary>
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
  static FormatCapability IImageFormatMetadata<PowerPointFile>.Capabilities => FormatCapability.MultiImage;
  static PowerPointFile IImageFormatReader<PowerPointFile>.FromSpan(ReadOnlySpan<byte> data)
    => _FromSpan(data);
  static PowerPointFile IImageFromRawImage<PowerPointFile>.FromRawImage(RawImage image, string extension)
    => FromRawImage(image, extension);
  static byte[] IImageFormatWriter<PowerPointFile>.ToBytes(PowerPointFile file)
    => _ToBytes(file);

  static VideoMode[] IImageFormatMetadata<PowerPointFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>CFB and ZIP are both shared containers, so either signature only means “possible”.</summary>
  static bool? IImageFormatMetadata<PowerPointFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (_IsZip(header))
      return null;
    if (header.Length < Signature.Length)
      return null;
    return header[..Signature.Length].SequenceEqual(Signature) ? null : false;
  }

  /// <summary>Compatibility dimensions and RGB data of the first extracted image.</summary>
  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];

  /// <summary>Every decodable picture carried by the file.</summary>
  public IReadOnlyList<RawImage> Images { get; init; } = [];

  internal PowerPointKind Kind { get; init; }

  public static int ImageCount(PowerPointFile file)
    => file.Images.Count > 0 ? file.Images.Count : _HasCompatibilityImage(file) ? 1 : 0;

  public static RawImage ToRawImage(PowerPointFile file, int index) {
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

  public static RawImage ToRawImage(PowerPointFile file) {
    if (ImageCount(file) == 0)
      throw new InvalidDataException("The PowerPoint file contains no decodable images.");
    return ToRawImage(file, 0);
  }

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
      Kind = PowerPointKindExtensions.FromExtension(extension),
    };
  }

  private static PowerPointFile _FromSpan(ReadOnlySpan<byte> data) {
    if (!_IsZip(data))
      return PowerPointReader.FromSpan(data);

    var result = OfficeOpenXmlImageReader.ReadAll(data, "ppt/media/", "/ppt/presentation.xml");
    var first = result.Images.Count > 0 ? result.Images[0].EnsureFormat(PixelFormat.Rgb24) : null;
    return new() {
      Width = first?.Width ?? 0,
      Height = first?.Height ?? 0,
      PixelData = first?.PixelData[..] ?? [],
      Images = result.Images,
      Kind = PowerPointKindExtensions.FromContentType(result.MainContentType),
    };
  }

  private static byte[] _ToBytes(PowerPointFile file) {
    if (!file.Kind.IsOpenXml())
      return PowerPointWriter.ToBytes(file);

    var image = ImageCount(file) > 0
      ? ToRawImage(file, 0).EnsureFormat(PixelFormat.Rgb24)
      : throw new InvalidDataException("PowerPoint file contains no picture to write.");

    return OfficeOpenXmlImagePackage.WritePowerPoint(
      image.Width, image.Height, image.PixelData, file.Kind.ContentType());
  }

  private static bool _HasCompatibilityImage(PowerPointFile file) {
    if (file.Width <= 0 || file.Height <= 0 || file.PixelData is null)
      return false;
    try {
      return file.PixelData.Length >= checked(file.Width * file.Height * 3);
    } catch (OverflowException) {
      return false;
    }
  }

  private static bool _IsZip(ReadOnlySpan<byte> data)
    => data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04;
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
