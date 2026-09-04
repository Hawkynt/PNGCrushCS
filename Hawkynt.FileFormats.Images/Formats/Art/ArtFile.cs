using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Art;

/// <summary>In-memory representation of a Build Engine ART tile archive.</summary>
public readonly record struct ArtFile : IImageFormatReader<ArtFile>, IImageToRawImage<ArtFile>, IImageFromRawImage<ArtFile>, IImageFormatWriter<ArtFile> {

  static string IImageFormatMetadata<ArtFile>.PrimaryExtension => ".art";
  static string[] IImageFormatMetadata<ArtFile>.FileExtensions => [".art"];
  static ArtFile IImageFormatReader<ArtFile>.FromSpan(ReadOnlySpan<byte> data) => ArtReader.FromSpan(data);
  static byte[] IImageFormatWriter<ArtFile>.ToBytes(ArtFile file) => ArtWriter.ToBytes(file);
  /// <summary>Starting tile number in the global tile index.</summary>
  public int TileStart { get; init; }

  /// <summary>Tiles contained in this ART file.</summary>
  public IReadOnlyList<ArtTile> Tiles { get; init; }

  /// <summary>Converts the first tile of an ART file to a <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// An ART archive stores indices only — the colours are in the game's own <c>palette.dat</c>,
  /// which this file neither contains nor names. The ramp from <see cref="IndexedPalette"/> stands
  /// in for it so the tile can be drawn; with a null palette the picture announced 256 colours and
  /// then threw on every conversion out of it.
  /// </remarks>
  public static RawImage ToRawImage(ArtFile file) {
    if (file.Tiles.Count == 0)
      throw new ArgumentException("ART file contains no tiles.", nameof(file));

    var tile = file.Tiles[0];
    return new RawImage {
      Width = tile.Width,
      Height = tile.Height,
      Format = PixelFormat.Indexed8,
      PixelData = tile.PixelData[..],
      Palette = IndexedPalette.GrayRamp(256),
      PaletteCount = 256
    };
  }

  /// <summary>Creates a single-tile ART file from an <see cref="RawImage"/>. Must be Indexed8.</summary>
  public static ArtFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);

    return new ArtFile {
      Tiles = [
        new ArtTile {
          Width = image.Width,
          Height = image.Height,
          PixelData = image.PixelData[..]
        }
      ]
    };
  }
}
