using System;
using FileFormat.Core;

namespace FileFormat.QuantumPaint;

/// <summary>In-memory representation of an Atari ST QuantumPaint image (320x200, 16 colors, 4 planes).</summary>
public readonly record struct QuantumPaintFile : IImageFormatReader<QuantumPaintFile>, IImageToRawImage<QuantumPaintFile>, IImageFromRawImage<QuantumPaintFile>, IImageFormatWriter<QuantumPaintFile> {

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
  /// <summary>Offset of the first palette block. QuantumPaint stores a table of 48-byte blocks
  /// between here and <see cref="PixelDataOffset"/>, each carrying 16 colours plus the scanline it
  /// takes effect on, which is how the format changes palette part-way down the screen.</summary>
  internal const int PaletteOffset = 128;

  /// <summary>Offset of the scanline byte inside a palette block.</summary>
  internal const int PaletteScanlineOffset = 33;

  /// <summary>Bitmap offset; the palette table occupies everything before it.</summary>
  internal const int PixelDataOffset = 512;

  internal const int MinFileSize = PixelDataOffset + PixelDataSize;

  static string IImageFormatMetadata<QuantumPaintFile>.PrimaryExtension => ".pbx";
  static string[] IImageFormatMetadata<QuantumPaintFile>.FileExtensions => [".pbx"];
  static QuantumPaintFile IImageFormatReader<QuantumPaintFile>.FromSpan(ReadOnlySpan<byte> data) => QuantumPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<QuantumPaintFile>.VideoModes => [new("Default", [(PixelWidth, PixelHeight)], [new IntegerRange(2, 16)])];
  static byte[] IImageFormatWriter<QuantumPaintFile>.ToBytes(QuantumPaintFile file) => QuantumPaintWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>16-entry palette of 12-bit Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST word-interleaved planar pixel data (4 planes).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(QuantumPaintFile file) {

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
    image = image.EnsureFormat(PixelFormat.Indexed8);
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
