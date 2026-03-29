using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ArtDirector;

/// <summary>In-memory representation of an Atari ST Art Director image (128-byte header + 32000 bytes planar data).</summary>
public sealed class ArtDirectorFile : IImageFileFormat<ArtDirectorFile> {

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

  static string IImageFileFormat<ArtDirectorFile>.PrimaryExtension => ".art";
  static string[] IImageFileFormat<ArtDirectorFile>.FileExtensions => [".art"];
  static FormatCapability IImageFileFormat<ArtDirectorFile>.Capabilities => FormatCapability.IndexedOnly;
  static ArtDirectorFile IImageFileFormat<ArtDirectorFile>.FromFile(FileInfo file) => ArtDirectorReader.FromFile(file);
  static ArtDirectorFile IImageFileFormat<ArtDirectorFile>.FromBytes(byte[] data) => ArtDirectorReader.FromBytes(data);
  static ArtDirectorFile IImageFileFormat<ArtDirectorFile>.FromStream(Stream stream) => ArtDirectorReader.FromStream(stream);
  static byte[] IImageFileFormat<ArtDirectorFile>.ToBytes(ArtDirectorFile file) => ArtDirectorWriter.ToBytes(file);

  /// <summary>Image width (depends on resolution).</summary>
  public int Width { get; init; } = 320;

  /// <summary>Image height (depends on resolution).</summary>
  public int Height { get; init; } = 200;

  /// <summary>Resolution: 0=low (320x200), 1=medium (640x200), 2=high (640x400).</summary>
  public short Resolution { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values.</summary>
  public short[] Palette { get; init; } = new short[16];

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(ArtDirectorFile file) {
    ArgumentNullException.ThrowIfNull(file);

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

  public static ArtDirectorFile FromRawImage(RawImage image) => throw new NotSupportedException("ArtDirector format does not support creation from RawImage.");
}
