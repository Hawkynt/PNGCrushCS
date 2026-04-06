using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Atari2600;

/// <summary>Atari 2600 TIA playfield graphics data model.</summary>
public sealed class Atari2600File : IImageFormatReader<Atari2600File>, IImageToRawImage<Atari2600File>, IImageFromRawImage<Atari2600File>, IImageFormatWriter<Atari2600File> {

  public const int BytesPerTile = 8;
  public const int TileSize = 8;
  public const int TilesPerRow = 16;

  public int Width { get; init; } = TilesPerRow * TileSize;
  public int Height { get; init; } = TileSize;
  public byte[] PixelData { get; init; } = [];
  public byte[] Palette { get; init; } = [0, 0, 0, 255, 255, 255];

  public static string PrimaryExtension => ".a26";
  public static string[] FileExtensions => [".a26", ".tia"];
  static Atari2600File IImageFormatReader<Atari2600File>.FromSpan(ReadOnlySpan<byte> data) => Atari2600Reader.FromSpan(data);
  public static FormatCapability Capabilities => FormatCapability.MonochromeOnly;
  public static Atari2600File FromFile(FileInfo file) => Atari2600Reader.FromFile(file);
  public static Atari2600File FromBytes(byte[] data) => Atari2600Reader.FromBytes(data);
  public static Atari2600File FromStream(Stream stream) => Atari2600Reader.FromStream(stream);
  public static byte[] ToBytes(Atari2600File file) => Atari2600Writer.ToBytes(file);

  public static RawImage ToRawImage(Atari2600File file) {
    ArgumentNullException.ThrowIfNull(file);
    var pixels = file.PixelData[..];
    return new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = file.Palette[..],
      PaletteCount = 2,
    };
  }

  public static Atari2600File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected Indexed8, got {image.Format}");
    if (image.Width % TileSize != 0 || image.Height % TileSize != 0)
      throw new ArgumentException($"Dimensions must be multiples of {TileSize}, got {image.Width}x{image.Height}");
    var pixels = image.PixelData[..];
    return new Atari2600File {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixels,
      Palette = image.Palette != null ? image.Palette[..] : [0, 0, 0, 255, 255, 255],
    };
  }
}
