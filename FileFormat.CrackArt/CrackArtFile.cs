using System;
using FileFormat.Core;

namespace FileFormat.CrackArt;

/// <summary>In-memory representation of a CrackArt packed image.</summary>
public readonly record struct CrackArtFile : IImageFormatReader<CrackArtFile>, IImageToRawImage<CrackArtFile>, IImageFromRawImage<CrackArtFile>, IImageFormatWriter<CrackArtFile> {

  static string IImageFormatMetadata<CrackArtFile>.PrimaryExtension => ".ca1";
  static string[] IImageFormatMetadata<CrackArtFile>.FileExtensions => [".ca1", ".ca2", ".ca3"];
  static CrackArtFile IImageFormatReader<CrackArtFile>.FromSpan(ReadOnlySpan<byte> data) => CrackArtReader.FromSpan(data);
  static byte[] IImageFormatWriter<CrackArtFile>.ToBytes(CrackArtFile file) => CrackArtWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public CrackArtResolution Resolution { get; init; }
  public short[] Palette { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(CrackArtFile file) {

    var numPlanes = file.Resolution switch {
      CrackArtResolution.Low => 4,
      CrackArtResolution.Medium => 2,
      CrackArtResolution.High => 1,
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

  public static CrackArtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));

    var resolution = image.PaletteCount switch {
      <= 2 => CrackArtResolution.High,
      <= 4 => CrackArtResolution.Medium,
      _ => CrackArtResolution.Low
    };

    var numPlanes = resolution switch {
      CrackArtResolution.Low => 4,
      CrackArtResolution.Medium => 2,
      CrackArtResolution.High => 1,
      _ => 4
    };

    var (width, height) = resolution switch {
      CrackArtResolution.High => (640, 400),
      CrackArtResolution.Medium => (640, 200),
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
