using System;
using FileFormat.Core;

namespace FileFormat.SegaGenTile;

/// <summary>In-memory representation of Sega Genesis/Mega Drive 4BPP tile data (8x8 tiles, 16 tiles per row).</summary>
public readonly record struct SegaGenTileFile : IImageFormatReader<SegaGenTileFile>, IImageToRawImage<SegaGenTileFile>, IImageFromRawImage<SegaGenTileFile>, IImageFormatWriter<SegaGenTileFile> {

  internal const int TileSize = 8;
  internal const int BytesPerTile = 32;
  internal const int TilesPerRow = 16;
  internal const int FixedWidth = TilesPerRow * TileSize;

  private static readonly byte[] _DefaultPalette = _BuildGrayscalePalette(16);

  static string IImageFormatMetadata<SegaGenTileFile>.PrimaryExtension => ".gen";
  static string[] IImageFormatMetadata<SegaGenTileFile>.FileExtensions => [".gen", ".sgd"];
  static SegaGenTileFile IImageFormatReader<SegaGenTileFile>.FromSpan(ReadOnlySpan<byte> data) => SegaGenTileReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<SegaGenTileFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<SegaGenTileFile>.ToBytes(SegaGenTileFile file) => SegaGenTileWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(SegaGenTileFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = 16,
    };
  }

  public static SegaGenTileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Sega Genesis tile requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != FixedWidth)
      throw new ArgumentException($"Sega Genesis tile requires width {FixedWidth}, got {image.Width}.", nameof(image));
    if (image.PaletteCount > 16)
      throw new ArgumentException($"Sega Genesis tile supports at most 16 palette entries, got {image.PaletteCount}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette != null ? image.Palette[..] : _DefaultPalette[..],
    };
  }

  private static byte[] _BuildGrayscalePalette(int count) {
    var palette = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      var v = (byte)(i * 255 / (count - 1));
      palette[i * 3] = v;
      palette[i * 3 + 1] = v;
      palette[i * 3 + 2] = v;
    }
    return palette;
  }
}
