using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.QuantumPaint;

/// <summary>In-memory representation of an Atari ST QuantumPaint image (320x200, 16 colors, 4 planes).</summary>
public sealed class QuantumPaintFile : IImageFileFormat<QuantumPaintFile> {

  /// <summary>Image width (always 320).</summary>
  internal const int PixelWidth = 320;

  /// <summary>Image height (always 200).</summary>
  internal const int PixelHeight = 200;

  /// <summary>Number of bitplanes.</summary>
  internal const int NumPlanes = 4;

  /// <summary>Size of the palette in bytes (16 entries x 2 bytes each).</summary>
  internal const int PaletteSize = 32;

  /// <summary>Size of the planar pixel data in bytes.</summary>
  internal const int PixelDataSize = 32000;

  /// <summary>Minimum file size (palette + pixel data).</summary>
  internal const int MinFileSize = PaletteSize + PixelDataSize;

  static string IImageFileFormat<QuantumPaintFile>.PrimaryExtension => ".pbx";
  static string[] IImageFileFormat<QuantumPaintFile>.FileExtensions => [".pbx"];
  static FormatCapability IImageFileFormat<QuantumPaintFile>.Capabilities => FormatCapability.IndexedOnly;
  static QuantumPaintFile IImageFileFormat<QuantumPaintFile>.FromFile(FileInfo file) => QuantumPaintReader.FromFile(file);
  static QuantumPaintFile IImageFileFormat<QuantumPaintFile>.FromBytes(byte[] data) => QuantumPaintReader.FromBytes(data);
  static QuantumPaintFile IImageFileFormat<QuantumPaintFile>.FromStream(Stream stream) => QuantumPaintReader.FromStream(stream);
  static byte[] IImageFileFormat<QuantumPaintFile>.ToBytes(QuantumPaintFile file) => QuantumPaintWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>16-entry palette of 12-bit Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST word-interleaved planar pixel data (4 planes).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(QuantumPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, PixelWidth, PixelHeight, NumPlanes);
    var paletteCount = Math.Min(16, file.Palette.Length);
    var rgb = PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, paletteCount));

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

  public static QuantumPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    if (image.Width != PixelWidth)
      throw new ArgumentException($"QuantumPaint images must be exactly {PixelWidth} pixels wide.", nameof(image));
    if (image.Height != PixelHeight)
      throw new ArgumentException($"QuantumPaint images must be exactly {PixelHeight} pixels tall.", nameof(image));

    var planar = PlanarConverter.ChunkyToAtariSt(image.PixelData, PixelWidth, PixelHeight, NumPlanes);
    var paletteCount = Math.Min(image.PaletteCount, 16);
    var stPalette = PlanarConverter.RgbToStPalette(image.Palette, paletteCount);
    var palette = new short[16];
    stPalette.AsSpan(0, Math.Min(stPalette.Length, 16)).CopyTo(palette);

    return new() {
      Palette = palette,
      PixelData = planar,
    };
  }
}
