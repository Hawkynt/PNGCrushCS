using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariPaintworks;

/// <summary>In-memory representation of an Atari ST Paintworks/GFA/DeskPic image file.</summary>
public sealed class AtariPaintworksFile : IImageFileFormat<AtariPaintworksFile> {

  static string IImageFileFormat<AtariPaintworksFile>.PrimaryExtension => ".cl0";
  static string[] IImageFileFormat<AtariPaintworksFile>.FileExtensions => [".cl0", ".cl1", ".cl2", ".pg0", ".pg1", ".pg2", ".pg3", ".sc0", ".sc1", ".sc2"];
  static FormatCapability IImageFileFormat<AtariPaintworksFile>.Capabilities => FormatCapability.IndexedOnly;
  static AtariPaintworksFile IImageFileFormat<AtariPaintworksFile>.FromFile(FileInfo file) => AtariPaintworksReader.FromFile(file);
  static AtariPaintworksFile IImageFileFormat<AtariPaintworksFile>.FromBytes(byte[] data) => AtariPaintworksReader.FromBytes(data);
  static AtariPaintworksFile IImageFileFormat<AtariPaintworksFile>.FromStream(Stream stream) => AtariPaintworksReader.FromStream(stream);
  static byte[] IImageFileFormat<AtariPaintworksFile>.ToBytes(AtariPaintworksFile file) => AtariPaintworksWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Resolution mode determining dimensions and color depth.</summary>
  public AtariPaintworksResolution Resolution { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>Atari ST word-interleaved planar pixel data (32000 bytes for full screen).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(AtariPaintworksFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var numPlanes = file.Resolution switch {
      AtariPaintworksResolution.Low => 4,
      AtariPaintworksResolution.Medium => 2,
      AtariPaintworksResolution.High => 1,
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

  public static AtariPaintworksFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));

    var resolution = image.PaletteCount switch {
      <= 2 => AtariPaintworksResolution.High,
      <= 4 => AtariPaintworksResolution.Medium,
      _ => AtariPaintworksResolution.Low
    };

    var numPlanes = resolution switch {
      AtariPaintworksResolution.Low => 4,
      AtariPaintworksResolution.Medium => 2,
      AtariPaintworksResolution.High => 1,
      _ => 4
    };

    var (width, height) = resolution switch {
      AtariPaintworksResolution.High => (640, 400),
      AtariPaintworksResolution.Medium => (640, 200),
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
