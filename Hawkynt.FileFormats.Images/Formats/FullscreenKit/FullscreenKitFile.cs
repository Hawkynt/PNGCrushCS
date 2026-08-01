using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FullscreenKit;

/// <summary>In-memory representation of an Atari ST Fullscreen Construction Kit overscan image (416x274 or 448x272, 16 colors, 4 planes).</summary>
public readonly record struct FullscreenKitFile : IImageFormatReader<FullscreenKitFile>, IImageToRawImage<FullscreenKitFile>, IImageFromRawImage<FullscreenKitFile>, IImageFormatWriter<FullscreenKitFile> {

  /// <summary>Number of bitplanes (always 4 for low resolution).</summary>
  public const int NumPlanes = 4;

  /// <summary>Number of usable palette colors (always 16 for 4 planes).</summary>
  public const int ColorCount = 16;

  /// <summary>Pixels across, which is the full overscan width rather than the visible one.</summary>
  /// <remarks>
  /// This was written as two variants, 416 by 274 and 448 by 272, and the format has neither. It is
  /// 448 by 274, and a row reserves six bytes more than its pixels need — so a file is 63054 bytes
  /// and neither of the two lengths this used to accept.
  /// </remarks>
  public const int PixelWidth = 448;

  /// <summary>Rows.</summary>
  public const int PixelHeight = 274;

  /// <summary>Bytes from one row of bitplanes to the next, which is wider than the pixels need.</summary>
  public const int Stride = 230;

  /// <summary>The two characters a file begins with.</summary>
  public const string Signature = "KD";

  /// <summary>Where the palette sits: straight after the signature.</summary>
  public const int PaletteOffset = 2;

  /// <summary>Where the bitplanes start.</summary>
  public const int BitmapOffset = PaletteOffset + ColorCount * 2;

  /// <summary>The exact length of a file.</summary>
  public const int FileSize = BitmapOffset + Stride * PixelHeight;

  static string IImageFormatMetadata<FullscreenKitFile>.PrimaryExtension => ".kid";
  static string[] IImageFormatMetadata<FullscreenKitFile>.FileExtensions => [".kid"];
  static FullscreenKitFile IImageFormatReader<FullscreenKitFile>.FromSpan(ReadOnlySpan<byte> data) => FullscreenKitReader.FromSpan(data);

  /// <summary>The size this format holds, which its writer requires and no other.</summary>
  /// <summary>Both overscan shapes the program produces, since the reader takes either.</summary>
  /// <remarks>
  /// Only the first was declared, so anything asking what this format can be written as was told
  /// half the answer — and the half it was told is not the one a reference decoder recognises.
  /// </remarks>
  static VideoMode[] IImageFormatMetadata<FullscreenKitFile>.VideoModes => [
    new("Overscan", [(PixelWidth, PixelHeight)], [ColorCount]),
  ];
  static byte[] IImageFormatWriter<FullscreenKitFile>.ToBytes(FullscreenKitFile file) => FullscreenKitWriter.ToBytes(file);

  /// <summary>Image width (416 or 448).</summary>
  public int Width { get; init; }

  /// <summary>Image height (274 or 272).</summary>
  public int Height { get; init; }

  /// <summary>16-entry palette of Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; }

  /// <summary>Atari ST word-interleaved 4-plane planar pixel data (overscan size).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Detects dimensions from the pixel data size after subtracting the palette header.</summary>
  public static RawImage ToRawImage(FullscreenKitFile file) {

    var chunky = AtariStGraphics.UnpackBitplanes(file.PixelData, 0, Stride, NumPlanes, file.Width, file.Height);
    var paletteCount = Math.Min(ColorCount, file.Palette.Length);
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

  public static FullscreenKitFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureIndexedAtMost(ColorCount);

    var width = image.Width;
    var height = image.Height;

    if (width != PixelWidth || height != PixelHeight)
      throw new ArgumentException(
        $"A Fullscreen Kit picture is {PixelWidth}x{PixelHeight}, got {width}x{height}.", nameof(image));

    // Packed at the file's own row stride, which reserves six bytes a row beyond the pixels.
    var planar = AtariStGraphics.PackBitplanes(image.PixelData, Stride, NumPlanes, width, height);
    var paletteCount = Math.Min(image.PaletteCount, 16);
    var stPalette = PlanarConverter.RgbToStPalette(image.Palette, paletteCount);
    var palette = new short[16];
    stPalette.AsSpan(0, Math.Min(stPalette.Length, 16)).CopyTo(palette);

    return new() {
      Width = width,
      Height = height,
      PixelData = planar,
      Palette = palette,
    };
  }
}
