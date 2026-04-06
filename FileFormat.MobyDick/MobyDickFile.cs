using System;
using FileFormat.Core;

namespace FileFormat.MobyDick;

/// <summary>In-memory representation of a Moby Dick paint image.</summary>
public readonly record struct MobyDickFile : IImageFormatReader<MobyDickFile>, IImageToRawImage<MobyDickFile>, IImageFormatWriter<MobyDickFile> {

  static string IImageFormatMetadata<MobyDickFile>.PrimaryExtension => ".mby";
  static string[] IImageFormatMetadata<MobyDickFile>.FileExtensions => [".mby", ".mbd"];
  static MobyDickFile IImageFormatReader<MobyDickFile>.FromSpan(ReadOnlySpan<byte> data) => MobyDickReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<MobyDickFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<MobyDickFile>.ToBytes(MobyDickFile file) => MobyDickWriter.ToBytes(file);

  /// <summary>The fixed width of a Moby Dick image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a Moby Dick image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (768 palette + 64000 pixel data).</summary>
  public const int ExpectedFileSize = 64768;

  /// <summary>Size of the palette section in bytes (256 entries x 3 bytes RGB).</summary>
  internal const int PaletteDataSize = 768;

  /// <summary>Size of the pixel data section in bytes (320 x 200).</summary>
  internal const int PixelDataSize = 64000;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>Palette data (768 bytes, 256 entries x 3 bytes RGB).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Pixel data (64000 bytes, 1 byte per pixel, index into palette).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this Moby Dick image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(MobyDickFile file) {

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var i = 0; i < PixelDataSize; ++i) {
      var colorIndex = file.PixelData[i];
      var palOffset = colorIndex * 3;
      var outOffset = i * 3;
      rgb[outOffset] = file.Palette[palOffset];
      rgb[outOffset + 1] = file.Palette[palOffset + 1];
      rgb[outOffset + 2] = file.Palette[palOffset + 2];
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
