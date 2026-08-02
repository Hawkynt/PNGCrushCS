using System;
using FileFormat.Core;

namespace FileFormat.Tiny;

/// <summary>In-memory representation of a Tiny (compressed DEGAS) image.</summary>
public readonly record struct TinyFile : IImageFormatReader<TinyFile>, IImageToRawImage<TinyFile>, IImageFromRawImage<TinyFile>, IImageFormatWriter<TinyFile> {

  static string IImageFormatMetadata<TinyFile>.PrimaryExtension => ".tny";
  static string[] IImageFormatMetadata<TinyFile>.FileExtensions => [".tny", ".tn1", ".tn2", ".tn3", ".tn4", ".tn5", ".tn6"];
  static TinyFile IImageFormatReader<TinyFile>.FromSpan(ReadOnlySpan<byte> data) => TinyReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<TinyFile>.VideoModes => [new("Default", [(320, 200), (640, 200), (640, 400)])];
  static byte[] IImageFormatWriter<TinyFile>.ToBytes(TinyFile file) => TinyWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public TinyResolution Resolution { get; init; }
  public short[] Palette { get; init; }
  public byte[] PixelData { get; init; }

  /// <summary>What a monochrome screen draws: paper then ink.</summary>
  private static readonly byte[] _MONOCHROME = [255, 255, 255, 0, 0, 0];

  public static RawImage ToRawImage(TinyFile file) {

    var numPlanes = file.Resolution switch {
      TinyResolution.Low => 4,
      TinyResolution.Medium => 2,
      TinyResolution.High => 1,
      _ => throw new ArgumentException($"Unsupported resolution: {file.Resolution}", nameof(file))
    };

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, file.Width, file.Height, numPlanes);
    var paletteCount = Math.Min(1 << numPlanes, file.Palette.Length);

    // High resolution is a monochrome screen: the Atari's palette registers do not colour it, and a
    // file that leaves something else in them — this one holds red in the second — is still black on
    // white. Reading the stored palette here paints the ink whatever happens to have been left there.
    var rgb = file.Resolution == TinyResolution.High
      ? _MONOCHROME
      : PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, paletteCount));

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

  public static TinyFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);

    var resolution = (image.Width, image.Height) switch {
      (640, 400) => TinyResolution.High,
      (640, 200) => TinyResolution.Medium,
      (320, 200) => TinyResolution.Low,
      _ => image.PaletteCount switch {
        <= 2 => TinyResolution.High,
        <= 4 => TinyResolution.Medium,
        _ => TinyResolution.Low
      }
    };

    var numPlanes = resolution switch {
      TinyResolution.Low => 4,
      TinyResolution.Medium => 2,
      TinyResolution.High => 1,
      _ => 4
    };

    var (width, height) = resolution switch {
      TinyResolution.High => (640, 400),
      TinyResolution.Medium => (640, 200),
      _ => (320, 200)
    };

    var planar = PlanarConverter.ChunkyToAtariSt(image.PixelData, width, height, numPlanes);
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
