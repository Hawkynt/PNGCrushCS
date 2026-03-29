using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Degas;

/// <summary>In-memory representation of a DEGAS/DEGAS Elite image.</summary>
public sealed class DegasFile : IImageFileFormat<DegasFile> {

  static string IImageFileFormat<DegasFile>.PrimaryExtension => ".pi1";
  static string[] IImageFileFormat<DegasFile>.FileExtensions => [".pi1", ".pi2", ".pi3", ".pc1", ".pc2", ".pc3"];
  static DegasFile IImageFileFormat<DegasFile>.FromFile(FileInfo file) => DegasReader.FromFile(file);
  static DegasFile IImageFileFormat<DegasFile>.FromBytes(byte[] data) => DegasReader.FromBytes(data);
  static DegasFile IImageFileFormat<DegasFile>.FromStream(Stream stream) => DegasReader.FromStream(stream);
  static byte[] IImageFileFormat<DegasFile>.ToBytes(DegasFile file) => DegasWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public DegasResolution Resolution { get; init; }
  public bool IsCompressed { get; init; }
  public short[] Palette { get; init; } = new short[16];
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(DegasFile file) {
    ArgumentNullException.ThrowIfNull(file);

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

    var resolution = image.PaletteCount switch {
      <= 2 => DegasResolution.High,
      <= 4 => DegasResolution.Medium,
      _ => DegasResolution.Low
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
