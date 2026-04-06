using System;
using FileFormat.Core;

namespace FileFormat.AtariGrafik;

/// <summary>In-memory representation of an Atari Grafik PCP image (320x200, 16 colors).</summary>
public readonly record struct AtariGrafikFile : IImageFormatReader<AtariGrafikFile>, IImageToRawImage<AtariGrafikFile>, IImageFormatWriter<AtariGrafikFile> {

  static string IImageFormatMetadata<AtariGrafikFile>.PrimaryExtension => ".pcp";
  static string[] IImageFormatMetadata<AtariGrafikFile>.FileExtensions => [".pcp"];
  static AtariGrafikFile IImageFormatReader<AtariGrafikFile>.FromSpan(ReadOnlySpan<byte> data) => AtariGrafikReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariGrafikFile>.ToBytes(AtariGrafikFile file) => AtariGrafikWriter.ToBytes(file);

  /// <summary>Header size: 1 word resolution + 16 words palette = 34 bytes.</summary>
  internal const int HeaderSize = 34;

  /// <summary>Pixel data size: 32000 bytes.</summary>
  internal const int PixelDataSize = 32000;

  /// <summary>Fixed total file size: 34 + 32000 = 32034 bytes.</summary>
  internal const int ExpectedFileSize = HeaderSize + PixelDataSize;

  /// <summary>Minimum valid file size (exact match required).</summary>
  public const int MinFileSize = ExpectedFileSize;

  /// <summary>Image width, always 320.</summary>
  public int Width => 320;

  /// <summary>Image height, always 200.</summary>
  public int Height => 200;

  /// <summary>Resolution word from header.</summary>
  public short Resolution { get; init; }

  /// <summary>16-entry palette of 12-bit STE RGB values.</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this Atari Grafik image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(AtariGrafikFile file) {

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, 320, 200, 4);
    var paletteCount = Math.Min(16, file.Palette.Length);
    var rgb = PlanarConverter.StPaletteToRgb(file.Palette.AsSpan(0, paletteCount));

    var pixelData = new byte[320 * 200 * 3];
    for (var i = 0; i < 320 * 200; ++i) {
      var index = chunky[i];
      var paletteOffset = index * 3;
      if (paletteOffset + 2 < rgb.Length) {
        pixelData[i * 3] = rgb[paletteOffset];
        pixelData[i * 3 + 1] = rgb[paletteOffset + 1];
        pixelData[i * 3 + 2] = rgb[paletteOffset + 2];
      }
    }

    return new() {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = pixelData,
    };
  }

}
