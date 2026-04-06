using System;
using FileFormat.Core;

namespace FileFormat.FreeHand;

/// <summary>In-memory representation of a FreeHand ST bitmap export image (Atari ST, 320x200, 16 colors).</summary>
public readonly record struct FreeHandFile : IImageFormatReader<FreeHandFile>, IImageToRawImage<FreeHandFile>, IImageFormatWriter<FreeHandFile> {

  public const int FileSize = 32034;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _WIDTH = 320;
  private const int _HEIGHT = 200;
  private const int _NUM_PLANES = 4;

  static string IImageFormatMetadata<FreeHandFile>.PrimaryExtension => ".fhs";
  static string[] IImageFormatMetadata<FreeHandFile>.FileExtensions => [".fhs"];
  static FreeHandFile IImageFormatReader<FreeHandFile>.FromSpan(ReadOnlySpan<byte> data) => FreeHandReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<FreeHandFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<FreeHandFile>.ToBytes(FreeHandFile file) => FreeHandWriter.ToBytes(file);

  /// <summary>Image width (always 320).</summary>
  public int Width { get; init; }

  /// <summary>Image height (always 200).</summary>
  public int Height { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FreeHandFile file) {

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, _WIDTH, _HEIGHT, _NUM_PLANES);
    var paletteCount = Math.Min(16, file.Palette.Length);
    var rgb = PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, paletteCount));

    return new() {
      Width = _WIDTH,
      Height = _HEIGHT,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = paletteCount,
    };
  }

}
