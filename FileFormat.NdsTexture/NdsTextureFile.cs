using System;
using FileFormat.Core;

namespace FileFormat.NdsTexture;

/// <summary>In-memory representation of a Nintendo DS 4bpp tile texture image.</summary>
public readonly record struct NdsTextureFile : IImageFormatReader<NdsTextureFile>, IImageToRawImage<NdsTextureFile>, IImageFromRawImage<NdsTextureFile>, IImageFormatWriter<NdsTextureFile> {

  internal const int BytesPerTile = 32;
  internal const int TileSize = 8;
  internal const int TilesPerRow = 16;
  internal const int BitsPerPixel = 4;
  internal const int PaletteColors = 16;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFormatMetadata<NdsTextureFile>.PrimaryExtension => ".nbfs";
  static string[] IImageFormatMetadata<NdsTextureFile>.FileExtensions => [".nbfs", ".nds"];
  static NdsTextureFile IImageFormatReader<NdsTextureFile>.FromSpan(ReadOnlySpan<byte> data) => NdsTextureReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<NdsTextureFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<NdsTextureFile>.ToBytes(NdsTextureFile file) => NdsTextureWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(NdsTextureFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = PaletteColors,
    };
  }

  public static NdsTextureFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"NdsTexture tile data requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Width != TilesPerRow * TileSize)
      throw new ArgumentException($"NdsTexture tile data requires width {TilesPerRow * TileSize}, got {image.Width}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette != null && image.Palette.Length >= PaletteColors * 3
        ? image.Palette[..]
        : _DefaultPalette[..],
    };
  }
}
