using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DaliST;

/// <summary>In-memory representation of an Atari ST Dali image (SD0/SD1/SD2).</summary>
public sealed class DaliSTFile : IImageFileFormat<DaliSTFile> {

  /// <summary>Palette size in bytes (16 words = 32 bytes).</summary>
  public const int PaletteSize = 32;

  /// <summary>Planar pixel data size.</summary>
  public const int PlanarDataSize = 32000;

  /// <summary>The exact file size: 32 + 32000 = 32032 bytes.</summary>
  public const int ExpectedFileSize = PaletteSize + PlanarDataSize;

  static string IImageFileFormat<DaliSTFile>.PrimaryExtension => ".sd0";
  static string[] IImageFileFormat<DaliSTFile>.FileExtensions => [".sd0", ".sd1", ".sd2"];
  static FormatCapability IImageFileFormat<DaliSTFile>.Capabilities => FormatCapability.IndexedOnly;
  static DaliSTFile IImageFileFormat<DaliSTFile>.FromFile(FileInfo file) => DaliSTReader.FromFile(file);
  static DaliSTFile IImageFileFormat<DaliSTFile>.FromBytes(byte[] data) => DaliSTReader.FromBytes(data);
  static DaliSTFile IImageFileFormat<DaliSTFile>.FromStream(Stream stream) => DaliSTReader.FromStream(stream);
  static byte[] IImageFileFormat<DaliSTFile>.ToBytes(DaliSTFile file) => DaliSTWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Resolution mode: Low (320x200, 4 planes), Medium (640x200, 2 planes), High (640x400, 1 plane).</summary>
  public DaliSTResolution Resolution { get; init; }

  /// <summary>16-entry palette of 12-bit Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(DaliSTFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var numPlanes = file.Resolution switch {
      DaliSTResolution.Low => 4,
      DaliSTResolution.Medium => 2,
      DaliSTResolution.High => 1,
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

  public static DaliSTFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));

    var resolution = image.PaletteCount switch {
      <= 2 => DaliSTResolution.High,
      <= 4 => DaliSTResolution.Medium,
      _ => DaliSTResolution.Low
    };

    var numPlanes = resolution switch {
      DaliSTResolution.Low => 4,
      DaliSTResolution.Medium => 2,
      DaliSTResolution.High => 1,
      _ => 4
    };

    var (width, height) = resolution switch {
      DaliSTResolution.High => (640, 400),
      DaliSTResolution.Medium => (640, 200),
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
