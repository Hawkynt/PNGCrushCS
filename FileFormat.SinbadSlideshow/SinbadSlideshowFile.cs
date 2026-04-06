using System;
using FileFormat.Core;

namespace FileFormat.SinbadSlideshow;

/// <summary>In-memory representation of an Atari ST Sinbad Slideshow image (320x200, 16 colors, 4 planes).</summary>
public readonly record struct SinbadSlideshowFile : IImageFormatReader<SinbadSlideshowFile>, IImageToRawImage<SinbadSlideshowFile>, IImageFromRawImage<SinbadSlideshowFile>, IImageFormatWriter<SinbadSlideshowFile> {

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

  /// <summary>Exact file size (palette + pixel data).</summary>
  internal const int FileSize = PaletteSize + PixelDataSize;

  static string IImageFormatMetadata<SinbadSlideshowFile>.PrimaryExtension => ".ssb";
  static string[] IImageFormatMetadata<SinbadSlideshowFile>.FileExtensions => [".ssb"];
  static SinbadSlideshowFile IImageFormatReader<SinbadSlideshowFile>.FromSpan(ReadOnlySpan<byte> data) => SinbadSlideshowReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<SinbadSlideshowFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<SinbadSlideshowFile>.ToBytes(SinbadSlideshowFile file) => SinbadSlideshowWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 200.</summary>
  public int Height => PixelHeight;

  /// <summary>16-entry palette of 12-bit Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST word-interleaved planar pixel data (4 planes).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SinbadSlideshowFile file) {

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

  public static SinbadSlideshowFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    if (image.Width != PixelWidth)
      throw new ArgumentException($"Sinbad Slideshow images must be exactly {PixelWidth} pixels wide.", nameof(image));
    if (image.Height != PixelHeight)
      throw new ArgumentException($"Sinbad Slideshow images must be exactly {PixelHeight} pixels tall.", nameof(image));

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
