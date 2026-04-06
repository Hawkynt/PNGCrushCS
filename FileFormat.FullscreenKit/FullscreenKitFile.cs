using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FullscreenKit;

/// <summary>In-memory representation of an Atari ST Fullscreen Construction Kit overscan image (416x274 or 448x272, 16 colors, 4 planes).</summary>
public readonly record struct FullscreenKitFile : IImageFormatReader<FullscreenKitFile>, IImageToRawImage<FullscreenKitFile>, IImageFromRawImage<FullscreenKitFile>, IImageFormatWriter<FullscreenKitFile> {

  /// <summary>Number of bitplanes (always 4 for low resolution).</summary>
  public const int NumPlanes = 4;

  /// <summary>Number of usable palette colors (always 16 for 4 planes).</summary>
  public const int ColorCount = 16;

  /// <summary>Primary variant: 416x274.</summary>
  public const int PrimaryWidth = 416;

  /// <summary>Primary variant: 416x274.</summary>
  public const int PrimaryHeight = 274;

  /// <summary>Alternate variant: 448x272.</summary>
  public const int AlternateWidth = 448;

  /// <summary>Alternate variant: 448x272.</summary>
  public const int AlternateHeight = 272;

  /// <summary>File size for the 416x274 variant: 32 + 208*274 = 57024.</summary>
  public const int PrimaryFileSize = FullscreenKitHeader.StructSize + PrimaryPixelDataSize;

  /// <summary>File size for the 448x272 variant: 32 + 224*272 = 60960.</summary>
  public const int AlternateFileSize = FullscreenKitHeader.StructSize + AlternatePixelDataSize;

  /// <summary>Pixel data size for 416x274: (416/16)*4*2*274 = 56992.</summary>
  internal const int PrimaryPixelDataSize = (PrimaryWidth / 16) * NumPlanes * 2 * PrimaryHeight;

  /// <summary>Pixel data size for 448x272: (448/16)*4*2*272 = 60928.</summary>
  internal const int AlternatePixelDataSize = (AlternateWidth / 16) * NumPlanes * 2 * AlternateHeight;

  static string IImageFormatMetadata<FullscreenKitFile>.PrimaryExtension => ".kid";
  static string[] IImageFormatMetadata<FullscreenKitFile>.FileExtensions => [".kid"];
  static FullscreenKitFile IImageFormatReader<FullscreenKitFile>.FromSpan(ReadOnlySpan<byte> data) => FullscreenKitReader.FromSpan(data);
  static byte[] IImageFormatWriter<FullscreenKitFile>.ToBytes(FullscreenKitFile file) => FullscreenKitWriter.ToBytes(file);

  /// <summary>Image width (416 or 448).</summary>
  public int Width { get; init; }

  /// <summary>Image height (274 or 272).</summary>
  public int Height { get; init; }

  /// <summary>16-entry palette of Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; }

  /// <summary>Atari ST word-interleaved 4-plane planar pixel data (overscan size).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Detects dimensions from the pixel data size after subtracting the palette header.</summary>
  internal static (int Width, int Height) DetectDimensions(int dataLength) {
    var pixelBytes = dataLength - FullscreenKitHeader.StructSize;
    if (pixelBytes == PrimaryPixelDataSize)
      return (PrimaryWidth, PrimaryHeight);
    if (pixelBytes == AlternatePixelDataSize)
      return (AlternateWidth, AlternateHeight);

    throw new InvalidDataException(
      $"Unrecognized Fullscreen Kit file size: {dataLength} bytes. " +
      $"Expected {PrimaryFileSize} (416x274) or {AlternateFileSize} (448x272)."
    );
  }

  public static RawImage ToRawImage(FullscreenKitFile file) {

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, file.Width, file.Height, NumPlanes);
    var paletteCount = Math.Min(ColorCount, file.Palette.Length);
    var rgb = PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, paletteCount));

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

  public static FullscreenKitFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));

    var width = image.Width;
    var height = image.Height;

    // Validate dimensions match one of the supported variants
    if (!((width == PrimaryWidth && height == PrimaryHeight) || (width == AlternateWidth && height == AlternateHeight)))
      throw new ArgumentException(
        $"Fullscreen Kit images must be {PrimaryWidth}x{PrimaryHeight} or {AlternateWidth}x{AlternateHeight}.",
        nameof(image)
      );

    var planar = PlanarConverter.ChunkyToAtariSt(image.PixelData, width, height, NumPlanes);
    var paletteCount = Math.Min(image.PaletteCount, 16);
    var stPalette = PlanarConverter.RgbToStPalette(image.Palette, paletteCount);
    var palette = new short[16];
    stPalette.AsSpan(0, Math.Min(stPalette.Length, 16)).CopyTo(palette);

    return new() {
      Width = width,
      Height = height,
      PixelData = planar,
      Palette = palette,
    };
  }
}
