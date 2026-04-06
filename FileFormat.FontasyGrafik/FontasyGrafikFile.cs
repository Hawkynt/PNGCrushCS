using System;
using FileFormat.Core;

namespace FileFormat.FontasyGrafik;

/// <summary>In-memory representation of an Atari ST Fontasy Grafik image (320x200, 4 planes, 16 colors).</summary>
public readonly record struct FontasyGrafikFile : IImageFormatReader<FontasyGrafikFile>, IImageToRawImage<FontasyGrafikFile>, IImageFormatWriter<FontasyGrafikFile> {

  /// <summary>Palette size in bytes (16 words = 32 bytes).</summary>
  public const int PaletteSize = 32;

  /// <summary>Padding size in bytes after palette.</summary>
  public const int PaddingSize = 2;

  /// <summary>Planar pixel data size.</summary>
  public const int PlanarDataSize = 32000;

  /// <summary>The exact file size: 32 + 2 + 32000 = 32034 bytes.</summary>
  public const int ExpectedFileSize = PaletteSize + PaddingSize + PlanarDataSize;

  static string IImageFormatMetadata<FontasyGrafikFile>.PrimaryExtension => ".bsg";
  static string[] IImageFormatMetadata<FontasyGrafikFile>.FileExtensions => [".bsg"];
  static FontasyGrafikFile IImageFormatReader<FontasyGrafikFile>.FromSpan(ReadOnlySpan<byte> data) => FontasyGrafikReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<FontasyGrafikFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<FontasyGrafikFile>.ToBytes(FontasyGrafikFile file) => FontasyGrafikWriter.ToBytes(file);

  /// <summary>Always 320.</summary>
  public int Width => 320;

  /// <summary>Always 200.</summary>
  public int Height => 200;

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FontasyGrafikFile file) {

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

}
