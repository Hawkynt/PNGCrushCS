using System;
using FileFormat.Core;

namespace FileFormat.Degas;

/// <summary>In-memory representation of a DEGAS/DEGAS Elite image.</summary>
public readonly record struct DegasFile : IImageFormatReader<DegasFile>, IImageToRawImage<DegasFile>, IImageFromRawImage<DegasFile>, IImageFormatWriter<DegasFile> {

  static string IImageFormatMetadata<DegasFile>.PrimaryExtension => ".pi1";
  static string[] IImageFormatMetadata<DegasFile>.FileExtensions => [".pi1", ".pi2", ".pi3", ".pc1", ".pc2", ".pc3"];
  static DegasFile IImageFormatReader<DegasFile>.FromSpan(ReadOnlySpan<byte> data) => DegasReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<DegasFile>.VideoModes => [
    new("Low resolution (320x200, 16 colours)", [(320, 200)], [new IntegerRange(2, 16)]),
    new("Medium resolution (640x200, 4 colours)", [(640, 200)], [new IntegerRange(2, 4)]),
    new("High resolution (640x400, monochrome)", [(640, 400)], [2])
  ];
  static byte[] IImageFormatWriter<DegasFile>.ToBytes(DegasFile file) => DegasWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public DegasResolution Resolution { get; init; }
  public bool IsCompressed { get; init; }
  public short[] Palette { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(DegasFile file) {

    var numPlanes = file.Resolution switch {
      DegasResolution.Low => 4,
      DegasResolution.Medium => 2,
      DegasResolution.High => 1,
      _ => throw new ArgumentException($"Unsupported resolution: {file.Resolution}", nameof(file))
    };

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, file.Width, file.Height, numPlanes);
    var paletteCount = Math.Min(1 << numPlanes, file.Palette.Length);
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

  public static DegasFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));

    // Resolve resolution from input dimensions when they match a valid Degas mode;
    // otherwise infer from palette size.
    var resolution = (image.Width, image.Height) switch {
      (320, 200) => DegasResolution.Low,
      (640, 200) => DegasResolution.Medium,
      (640, 400) => DegasResolution.High,
      _ => image.PaletteCount switch {
        <= 2 => DegasResolution.High,
        <= 4 => DegasResolution.Medium,
        _ => DegasResolution.Low
      }
    };

    var numPlanes = resolution switch {
      DegasResolution.Low => 4,
      DegasResolution.Medium => 2,
      DegasResolution.High => 1,
      _ => 4
    };

    var (width, height) = resolution switch {
      DegasResolution.High => (640, 400),
      DegasResolution.Medium => (640, 200),
      _ => (320, 200)
    };

    // If supplied PixelData doesn't match target dimensions, pad/crop to fit.
    var expectedPixels = width * height;
    var srcPixels = image.PixelData ?? [];
    byte[] adjustedPixels;
    if (srcPixels.Length == expectedPixels) {
      adjustedPixels = srcPixels;
    } else {
      adjustedPixels = new byte[expectedPixels];
      var copy = Math.Min(srcPixels.Length, expectedPixels);
      Array.Copy(srcPixels, adjustedPixels, copy);
    }

    var planar = PlanarConverter.ChunkyToAtariSt(adjustedPixels, width, height, numPlanes);
    var paletteCount = Math.Min(image.PaletteCount, 16);
    var stPalette = PlanarConverter.RgbToStPalette(image.Palette, paletteCount);
    var palette = new short[16];
    stPalette.AsSpan(0, Math.Min(stPalette.Length, 16)).CopyTo(palette);

    return new() {
      Width = width,
      Height = height,
      Resolution = resolution,
      PixelData = planar,
      Palette = palette,
    };
  }
}
