using System;
using FileFormat.Core;

namespace FileFormat.CompuServeRle;

/// <summary>In-memory representation of the original CompuServe monochrome RLE graphics format.</summary>
/// <remarks>
/// Standard CompuServe RLE has two fixed display modes: medium resolution 128x96 and high resolution
/// 256x192. Raster bits are stored here MSB-left with one for foreground/white and zero for
/// background/black; the terminal stream itself represents those pixels as alternating run lengths.
/// </remarks>
[FormatMagicBytes([0x1B, (byte)'G', (byte)'M'])]
[FormatMagicBytes([0x1B, (byte)'G', (byte)'H'])]
public readonly record struct CompuServeRleFile
  : IImageFormatReader<CompuServeRleFile>, IImageToRawImage<CompuServeRleFile>,
    IImageFromRawImage<CompuServeRleFile>, IImageFormatWriter<CompuServeRleFile> {

  /// <summary>Medium-resolution width.</summary>
  public const int MediumWidth = 128;

  /// <summary>Medium-resolution height.</summary>
  public const int MediumHeight = 96;

  /// <summary>High-resolution width.</summary>
  public const int HighWidth = 256;

  /// <summary>High-resolution height.</summary>
  public const int HighHeight = 192;

  static string IImageFormatMetadata<CompuServeRleFile>.PrimaryExtension => ".rle";
  static string[] IImageFormatMetadata<CompuServeRleFile>.FileExtensions => [".rle"];
  static CompuServeRleFile IImageFormatReader<CompuServeRleFile>.FromSpan(ReadOnlySpan<byte> data)
    => CompuServeRleReader.FromSpan(data);
  static byte[] IImageFormatWriter<CompuServeRleFile>.ToBytes(CompuServeRleFile file)
    => CompuServeRleWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<CompuServeRleFile>.VideoModes => [
    new("Medium resolution", [(MediumWidth, MediumHeight)], [2]),
    new("High resolution", [(HighWidth, HighHeight)], [2]),
  ];

  /// <summary>Image width in pixels; either 128 or 256.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels; either 96 or 192.</summary>
  public int Height { get; init; }

  /// <summary>Packed MSB-left monochrome rows; one is foreground/white, zero is background/black.</summary>
  public byte[] RasterData { get; init; }

  /// <summary>Gets the packed byte count occupied by one row.</summary>
  public static int GetRowStride(int width) => width >> 3;

  /// <summary>Converts the CompuServe raster to a black/white image.</summary>
  public static RawImage ToRawImage(CompuServeRleFile file) {
    Validate(file, nameof(file));
    return MonochromePage.Decode(file.RasterData, file.Width, file.Height, inkIsWhite: true);
  }

  /// <summary>Creates a standard CompuServe RLE image from one of its two exact display geometries.</summary>
  public static CompuServeRleFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    ValidateDimensions(image.Width, image.Height, nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      RasterData = MonochromePage.Encode(image, image.Width, image.Height, inkIsWhite: true),
    };
  }

  internal static void Validate(CompuServeRleFile file, string parameterName) {
    ValidateDimensions(file.Width, file.Height, parameterName);
    var expected = checked(GetRowStride(file.Width) * file.Height);
    if (file.RasterData is null || file.RasterData.Length != expected)
      throw new ArgumentException($"CompuServe RLE raster length must be exactly {expected} bytes.", parameterName);
  }

  internal static void ValidateDimensions(int width, int height, string parameterName) {
    var isMedium = width == MediumWidth && height == MediumHeight;
    var isHigh = width == HighWidth && height == HighHeight;
    if (!isMedium && !isHigh)
      throw new ArgumentOutOfRangeException(parameterName,
        $"CompuServe RLE supports only {MediumWidth}x{MediumHeight} or {HighWidth}x{HighHeight} pixels.");
  }
}
