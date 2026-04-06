using System;
using FileFormat.Core;

namespace FileFormat.EzArt;

/// <summary>In-memory representation of an EZ-Art Professional image (Atari ST, 320x200, 16 colors).</summary>
public readonly record struct EzArtFile : IImageFormatReader<EzArtFile>, IImageToRawImage<EzArtFile>, IImageFromRawImage<EzArtFile>, IImageFormatWriter<EzArtFile> {

  /// <summary>Expected file size: 32 bytes palette + 32000 bytes pixel data.</summary>
  public const int FileSize = 32032;

  static string IImageFormatMetadata<EzArtFile>.PrimaryExtension => ".eza";
  static string[] IImageFormatMetadata<EzArtFile>.FileExtensions => [".eza"];
  static EzArtFile IImageFormatReader<EzArtFile>.FromSpan(ReadOnlySpan<byte> data) => EzArtReader.FromSpan(data);
  static byte[] IImageFormatWriter<EzArtFile>.ToBytes(EzArtFile file) => EzArtWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; }

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; }

  /// <summary>16-entry palette of 12-bit Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data (4 planes).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(EzArtFile file) {

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

  public static EzArtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    if (image.Width != 320)
      throw new ArgumentException("EZ-Art images must be exactly 320 pixels wide.", nameof(image));
    if (image.Height != 200)
      throw new ArgumentException("EZ-Art images must be exactly 200 pixels tall.", nameof(image));

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
