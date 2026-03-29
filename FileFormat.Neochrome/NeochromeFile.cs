using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Neochrome;

/// <summary>In-memory representation of an Atari ST NEOchrome image (320x200, 16 colors).</summary>
public sealed class NeochromeFile : IImageFileFormat<NeochromeFile> {

  static string IImageFileFormat<NeochromeFile>.PrimaryExtension => ".neo";
  static string[] IImageFileFormat<NeochromeFile>.FileExtensions => [".neo"];
  static NeochromeFile IImageFileFormat<NeochromeFile>.FromFile(FileInfo file) => NeochromeReader.FromFile(file);
  static NeochromeFile IImageFileFormat<NeochromeFile>.FromBytes(byte[] data) => NeochromeReader.FromBytes(data);
  static NeochromeFile IImageFileFormat<NeochromeFile>.FromStream(Stream stream) => NeochromeReader.FromStream(stream);
  static byte[] IImageFileFormat<NeochromeFile>.ToBytes(NeochromeFile file) => NeochromeWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; } = 320;

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; } = 200;

  /// <summary>Header flag word (typically 0).</summary>
  public short Flag { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>Animation speed.</summary>
  public byte AnimSpeed { get; init; }

  /// <summary>Animation direction.</summary>
  public byte AnimDirection { get; init; }

  /// <summary>Number of animation steps.</summary>
  public short AnimSteps { get; init; }

  /// <summary>Animation X offset.</summary>
  public short AnimXOffset { get; init; }

  /// <summary>Animation Y offset.</summary>
  public short AnimYOffset { get; init; }

  /// <summary>Animation width.</summary>
  public short AnimWidth { get; init; }

  /// <summary>Animation height.</summary>
  public short AnimHeight { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; } = new byte[32000];

  public static RawImage ToRawImage(NeochromeFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, 320, 200, 4);
    var paletteCount = Math.Min(16, file.Palette.Length);
    var rgb = PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, paletteCount));

    return new() {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

  public static NeochromeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    if (image.Width != 320)
      throw new ArgumentException("NEOchrome images must be exactly 320 pixels wide.", nameof(image));
    if (image.Height != 200)
      throw new ArgumentException("NEOchrome images must be exactly 200 pixels tall.", nameof(image));

    var planar = PlanarConverter.ChunkyToAtariSt(image.PixelData, 320, 200, 4);
    var paletteCount = Math.Min(image.PaletteCount, 16);
    var stPalette = PlanarConverter.RgbToStPalette(image.Palette, paletteCount);
    var palette = new short[16];
    stPalette.AsSpan(0, Math.Min(stPalette.Length, 16)).CopyTo(palette);

    return new() {
      Width = 320,
      Height = 200,
      PixelData = planar,
      Palette = palette,
    };
  }
}
