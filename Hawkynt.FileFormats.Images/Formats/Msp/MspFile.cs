using System;
using FileFormat.Core;

namespace FileFormat.Msp;

/// <summary>In-memory representation of a Microsoft Paint (MSP) image.</summary>
[FormatDetectionPriority(20)]
[FormatMagicBytes([0x44, 0x61, 0x6E, 0x4D])]
[FormatMagicBytes([0x4C, 0x69, 0x6E, 0x53])]
public readonly record struct MspFile : IImageFormatReader<MspFile>, IImageToRawImage<MspFile>, IImageFromRawImage<MspFile>, IImageFormatWriter<MspFile> {

  /// <summary>Largest decoded image accepted before allocating its raster.</summary>
  public const int MaximumPixels = 100_000_000;

  static string IImageFormatMetadata<MspFile>.PrimaryExtension => ".msp";
  static string[] IImageFormatMetadata<MspFile>.FileExtensions => [".msp"];
  static MspFile IImageFormatReader<MspFile>.FromSpan(ReadOnlySpan<byte> data) => MspReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MspFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<MspFile>.ToBytes(MspFile file) => MspWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public MspVersion Version { get; init; }

  /// <summary>Horizontal aspect ratio field for the source bitmap device.</summary>
  public ushort XAspect { get; init; }

  /// <summary>Vertical aspect ratio field for the source bitmap device.</summary>
  public ushort YAspect { get; init; }

  /// <summary>Horizontal aspect ratio field for the printer/output device.</summary>
  public ushort XAspectPrinter { get; init; }

  /// <summary>Vertical aspect ratio field for the printer/output device.</summary>
  public ushort YAspectPrinter { get; init; }

  /// <summary>Printer/output-device width field in pixels.</summary>
  public ushort PrinterWidth { get; init; }

  /// <summary>Printer/output-device height field in pixels.</summary>
  public ushort PrinterHeight { get; init; }

  /// <summary>Unused horizontal aspect-correction field, preserved for round trips.</summary>
  public ushort XAspectCorr { get; init; }

  /// <summary>Unused vertical aspect-correction field, preserved for round trips.</summary>
  public ushort YAspectCorr { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(MspFile file) {
    Validate(file, nameof(file));
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = file.PixelData[..],
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static MspFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    ValidateDimensions(image.Width, image.Height, nameof(image));
    image = image.EnsureFormat(PixelFormat.Indexed1);

    var width = checked((ushort)image.Width);
    var height = checked((ushort)image.Height);
    return new() {
      Width = image.Width,
      Height = image.Height,
      Version = MspVersion.V2,
      XAspect = width,
      YAspect = height,
      XAspectPrinter = width,
      YAspectPrinter = height,
      PrinterWidth = width,
      PrinterHeight = height,
      PixelData = image.PixelData[..],
    };
  }

  internal static int GetRowStride(int width) => (width + 7) >> 3;

  internal static void Validate(MspFile file, string parameterName) {
    ValidateDimensions(file.Width, file.Height, parameterName);
    if (file.Version is not MspVersion.V1 and not MspVersion.V2)
      throw new ArgumentOutOfRangeException(parameterName, "Unsupported MSP version.");

    var expected = checked(GetRowStride(file.Width) * file.Height);
    if (file.PixelData is null || file.PixelData.Length != expected)
      throw new ArgumentException($"MSP pixel data length must be exactly {expected} bytes.", parameterName);
  }

  internal static void ValidateDimensions(int width, int height, string parameterName) {
    if (width <= 0 || width > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(parameterName, $"MSP width must be in the range 1..{ushort.MaxValue}.");
    if (height <= 0 || height > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(parameterName, $"MSP height must be in the range 1..{ushort.MaxValue}.");
    if ((long)width * height > MaximumPixels)
      throw new ArgumentOutOfRangeException(parameterName, $"MSP images may not exceed {MaximumPixels:N0} pixels.");
  }
}
