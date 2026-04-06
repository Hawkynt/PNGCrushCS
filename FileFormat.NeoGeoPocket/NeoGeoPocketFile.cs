using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.NeoGeoPocket;

/// <summary>Neo Geo Pocket Color 2bpp tile data data model.</summary>
public sealed class NeoGeoPocketFile : IImageFormatReader<NeoGeoPocketFile>, IImageToRawImage<NeoGeoPocketFile>, IImageFromRawImage<NeoGeoPocketFile>, IImageFormatWriter<NeoGeoPocketFile> {

  public const int BytesPerTile = 16;
  public const int TileSize = 8;
  public const int TilesPerRow = 16;

  public int Width { get; init; } = TilesPerRow * TileSize;
  public int Height { get; init; } = TileSize;
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = [224, 248, 208, 136, 192, 112, 52, 104, 86, 8, 24, 32];

  public static string PrimaryExtension => ".ngp";
  public static string[] FileExtensions => [".ngp", ".ngpc"];
  static NeoGeoPocketFile IImageFormatReader<NeoGeoPocketFile>.FromSpan(ReadOnlySpan<byte> data) => NeoGeoPocketReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<NeoGeoPocketFile>.Capabilities => FormatCapability.IndexedOnly;
  public static NeoGeoPocketFile FromFile(FileInfo file) => NeoGeoPocketReader.FromFile(file);
  public static NeoGeoPocketFile FromBytes(byte[] data) => NeoGeoPocketReader.FromBytes(data);
  public static NeoGeoPocketFile FromStream(Stream stream) => NeoGeoPocketReader.FromStream(stream);
  public static byte[] ToBytes(NeoGeoPocketFile file) => NeoGeoPocketWriter.ToBytes(file);

  public static RawImage ToRawImage(NeoGeoPocketFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixels = file.PixelData[..];
    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = file.Palette[..],
      PaletteCount = 4,
    };
  }

  public static NeoGeoPocketFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8, got {image.Format}");
    if (image.Width % TileSize != 0 || image.Height % TileSize != 0)
      throw new ArgumentException($"Dimensions must be multiples of {TileSize}, got {image.Width}x{image.Height}");
    var pixels = image.PixelData[..];
    return new NeoGeoPocketFile {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixels,
      Palette = image.Palette != null ? image.Palette[..] : [224, 248, 208, 136, 192, 112, 52, 104, 86, 8, 24, 32],
    };
  }
}
