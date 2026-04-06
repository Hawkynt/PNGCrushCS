using System;
using FileFormat.Core;

namespace FileFormat.SyntheticArts;

/// <summary>In-memory representation of an Atari ST Synthetic Arts image (640x200, 4 colors, 2-plane medium resolution).</summary>
public readonly record struct SyntheticArtsFile : IImageFormatReader<SyntheticArtsFile>, IImageToRawImage<SyntheticArtsFile>, IImageFromRawImage<SyntheticArtsFile>, IImageFormatWriter<SyntheticArtsFile> {

  /// <summary>Total file size: 32-byte palette + 32000 bytes planar data.</summary>
  public const int FileSize = SyntheticArtsHeader.StructSize + 32000;

  /// <summary>Image width (always 640).</summary>
  public const int ImageWidth = 640;

  /// <summary>Image height (always 200).</summary>
  public const int ImageHeight = 200;

  /// <summary>Number of bitplanes (always 2 for medium resolution).</summary>
  public const int NumPlanes = 2;

  /// <summary>Number of usable palette colors (always 4 for 2 planes).</summary>
  public const int ColorCount = 4;

  static string IImageFormatMetadata<SyntheticArtsFile>.PrimaryExtension => ".srt";
  static string[] IImageFormatMetadata<SyntheticArtsFile>.FileExtensions => [".srt"];
  static SyntheticArtsFile IImageFormatReader<SyntheticArtsFile>.FromSpan(ReadOnlySpan<byte> data) => SyntheticArtsReader.FromSpan(data);
  static byte[] IImageFormatWriter<SyntheticArtsFile>.ToBytes(SyntheticArtsFile file) => SyntheticArtsWriter.ToBytes(file);

  /// <summary>16-entry palette of Atari ST RGB values (0x0RGB, R/G/B in 0-7). Only the first 4 entries are used.</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST word-interleaved 2-plane planar pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SyntheticArtsFile file) {

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, ImageWidth, ImageHeight, NumPlanes);
    var paletteCount = Math.Min(ColorCount, file.Palette.Length);
    var rgb = PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, paletteCount));

    return new() {
      Width = ImageWidth,
      Height = ImageHeight,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

  public static SyntheticArtsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    if (image.Width != ImageWidth)
      throw new ArgumentException($"Synthetic Arts images must be exactly {ImageWidth} pixels wide.", nameof(image));
    if (image.Height != ImageHeight)
      throw new ArgumentException($"Synthetic Arts images must be exactly {ImageHeight} pixels tall.", nameof(image));

    var planar = PlanarConverter.ChunkyToAtariSt(image.PixelData, ImageWidth, ImageHeight, NumPlanes);
    var paletteCount = Math.Min(image.PaletteCount, 16);
    var stPalette = PlanarConverter.RgbToStPalette(image.Palette, paletteCount);
    var palette = new short[16];
    stPalette.AsSpan(0, Math.Min(stPalette.Length, 16)).CopyTo(palette);

    return new() {
      PixelData = planar,
      Palette = palette,
    };
  }
}
