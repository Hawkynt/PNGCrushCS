using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Tiny;

/// <summary>In-memory representation of a Tiny (compressed DEGAS) image.</summary>
public sealed class TinyFile : IImageFileFormat<TinyFile> {

  static string IImageFileFormat<TinyFile>.PrimaryExtension => ".tny";
  static string[] IImageFileFormat<TinyFile>.FileExtensions => [".tny", ".tn1", ".tn2", ".tn3"];
  static TinyFile IImageFileFormat<TinyFile>.FromFile(FileInfo file) => TinyReader.FromFile(file);
  static TinyFile IImageFileFormat<TinyFile>.FromBytes(byte[] data) => TinyReader.FromBytes(data);
  static TinyFile IImageFileFormat<TinyFile>.FromStream(Stream stream) => TinyReader.FromStream(stream);
  static byte[] IImageFileFormat<TinyFile>.ToBytes(TinyFile file) => TinyWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public TinyResolution Resolution { get; init; }
  public short[] Palette { get; init; } = new short[16];
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(TinyFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var numPlanes = file.Resolution switch {
      TinyResolution.Low => 4,
      TinyResolution.Medium => 2,
      TinyResolution.High => 1,
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

  public static TinyFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));

    var resolution = image.PaletteCount switch {
      <= 2 => TinyResolution.High,
      <= 4 => TinyResolution.Medium,
      _ => TinyResolution.Low
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
