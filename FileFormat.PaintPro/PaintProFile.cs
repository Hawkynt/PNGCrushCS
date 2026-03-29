using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PaintPro;

/// <summary>In-memory representation of a Paint Pro image (Atari ST, 320x200, 16 colors).</summary>
public sealed class PaintProFile : IImageFileFormat<PaintProFile> {

  /// <summary>Expected file size: 2-byte resolution + 32-byte palette + 32000 bytes planar data.</summary>
  public const int FileSize = 32034;

  static string IImageFileFormat<PaintProFile>.PrimaryExtension => ".ppro";
  static string[] IImageFileFormat<PaintProFile>.FileExtensions => [".ppro"];
  static FormatCapability IImageFileFormat<PaintProFile>.Capabilities => FormatCapability.IndexedOnly;
  static PaintProFile IImageFileFormat<PaintProFile>.FromFile(FileInfo file) => PaintProReader.FromFile(file);
  static PaintProFile IImageFileFormat<PaintProFile>.FromBytes(byte[] data) => PaintProReader.FromBytes(data);
  static PaintProFile IImageFileFormat<PaintProFile>.FromStream(Stream stream) => PaintProReader.FromStream(stream);
  static byte[] IImageFileFormat<PaintProFile>.ToBytes(PaintProFile file) => PaintProWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; } = 320;

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; } = 200;

  /// <summary>Resolution word (0 = low 320x200).</summary>
  public ushort Resolution { get; init; }

  /// <summary>16-entry palette of Atari ST 9-bit RGB values.</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST word-interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; } = new byte[32000];

  public static RawImage ToRawImage(PaintProFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, file.Width, file.Height, 4);
    var paletteCount = Math.Min(16, file.Palette.Length);
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

  public static PaintProFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    if (image.Width != 320)
      throw new ArgumentException("Paint Pro images must be exactly 320 pixels wide.", nameof(image));
    if (image.Height != 200)
      throw new ArgumentException("Paint Pro images must be exactly 200 pixels tall.", nameof(image));

    var planar = PlanarConverter.ChunkyToAtariSt(image.PixelData, 320, 200, 4);
    var paletteCount = Math.Min(image.PaletteCount, 16);
    var stPalette = PlanarConverter.RgbToStPalette(image.Palette, paletteCount);
    var palette = new short[16];
    stPalette.AsSpan(0, Math.Min(stPalette.Length, 16)).CopyTo(palette);

    return new() {
      Width = 320,
      Height = 200,
      Resolution = 0,
      PixelData = planar,
      Palette = palette,
    };
  }
}
