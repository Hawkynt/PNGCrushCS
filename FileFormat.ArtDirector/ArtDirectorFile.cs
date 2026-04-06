using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.ArtDirector;

/// <summary>In-memory representation of an Atari ST Art Director image (128-byte header + 32000 bytes planar data).</summary>
public readonly record struct ArtDirectorFile : IImageFormatReader<ArtDirectorFile>, IImageToRawImage<ArtDirectorFile>, IImageFormatWriter<ArtDirectorFile> {

  /// <summary>Header size in bytes.</summary>
  public const int HeaderSize = 128;

  /// <summary>Offset of the palette within the header.</summary>
  public const int PaletteOffset = 2;

  /// <summary>Palette size in bytes (16 words = 32 bytes).</summary>
  public const int PaletteSize = 32;

  /// <summary>Planar pixel data size.</summary>
  public const int PlanarDataSize = 32000;

  /// <summary>The exact file size: 128 + 32000 = 32128 bytes.</summary>
  public const int ExpectedFileSize = HeaderSize + PlanarDataSize;

  static string IImageFormatMetadata<ArtDirectorFile>.PrimaryExtension => ".art";
  static string[] IImageFormatMetadata<ArtDirectorFile>.FileExtensions => [".art"];
  static ArtDirectorFile IImageFormatReader<ArtDirectorFile>.FromSpan(ReadOnlySpan<byte> data) => ArtDirectorReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<ArtDirectorFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<ArtDirectorFile>.ToBytes(ArtDirectorFile file) => ArtDirectorWriter.ToBytes(file);

  /// <summary>Image width (depends on resolution).</summary>
  public int Width { get; init; }

  /// <summary>Image height (depends on resolution).</summary>
  public int Height { get; init; }

  /// <summary>Resolution: 0=low (320x200), 1=medium (640x200), 2=high (640x400).</summary>
  public short Resolution { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(ArtDirectorFile file) {

    var numPlanes = file.Resolution switch {
      0 => 4,
      1 => 2,
      2 => 1,
      _ => 4
    };

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, file.Width, file.Height, numPlanes);
    var paletteCount = Math.Min(1 << numPlanes, file.Palette.Length);
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

}
