using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.NdsTexture;

/// <summary>In-memory representation of a Nintendo DS 4bpp tile texture image.</summary>
public sealed class NdsTextureFile : IImageFileFormat<NdsTextureFile> {

  internal const int BytesPerTile = 32;
  internal const int TileSize = 8;
  internal const int TilesPerRow = 16;
  internal const int BitsPerPixel = 4;
  internal const int PaletteColors = 16;

  private static readonly byte[] _DefaultPalette = [0, 0, 0, 0, 0, 170, 0, 170, 0, 0, 170, 170, 170, 0, 0, 170, 0, 170, 170, 85, 0, 170, 170, 170, 85, 85, 85, 85, 85, 255, 85, 255, 85, 85, 255, 255, 255, 85, 85, 255, 85, 255, 255, 255, 85, 255, 255, 255];

  static string IImageFileFormat<NdsTextureFile>.PrimaryExtension => ".nbfs";
  static string[] IImageFileFormat<NdsTextureFile>.FileExtensions => [".nbfs", ".nds"];
  static FormatCapability IImageFileFormat<NdsTextureFile>.Capabilities => FormatCapability.IndexedOnly;
  static NdsTextureFile IImageFileFormat<NdsTextureFile>.FromFile(FileInfo file) => NdsTextureReader.FromFile(file);
  static NdsTextureFile IImageFileFormat<NdsTextureFile>.FromBytes(byte[] data) => NdsTextureReader.FromBytes(data);
  static NdsTextureFile IImageFileFormat<NdsTextureFile>.FromStream(Stream stream) => NdsTextureReader.FromStream(stream);
  static byte[] IImageFileFormat<NdsTextureFile>.ToBytes(NdsTextureFile file) => NdsTextureWriter.ToBytes(file);

  public int Width { get; init; } = TilesPerRow * TileSize;
  public int Height { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = _DefaultPalette[..];

  public static RawImage ToRawImage(NdsTextureFile file) {
    ArgumentNullException.ThrowIfNull(file);
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
