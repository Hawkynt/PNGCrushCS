using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AppleShr;

/// <summary>In-memory representation of an Apple IIgs Super Hi-Res image.</summary>
public sealed class AppleShrFile : IImageFileFormat<AppleShrFile> {

  static string IImageFileFormat<AppleShrFile>.PrimaryExtension => ".shr";
  static string[] IImageFileFormat<AppleShrFile>.FileExtensions => [".shr"];
  static FormatCapability IImageFileFormat<AppleShrFile>.Capabilities => FormatCapability.IndexedOnly;
  static AppleShrFile IImageFileFormat<AppleShrFile>.FromFile(FileInfo file) => AppleShrReader.FromFile(file);
  static AppleShrFile IImageFileFormat<AppleShrFile>.FromBytes(byte[] data) => AppleShrReader.FromBytes(data);
  static AppleShrFile IImageFileFormat<AppleShrFile>.FromStream(Stream stream) => AppleShrReader.FromStream(stream);
  static byte[] IImageFileFormat<AppleShrFile>.ToBytes(AppleShrFile file) => AppleShrWriter.ToBytes(file);

  /// <summary>The fixed width of a Super Hi-Res image in pixels (320 mode).</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of a Super Hi-Res image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>The expected total file size in bytes (32000 pixel + 200 SCB + 56 padding + 512 palette).</summary>
  public const int ExpectedFileSize = 32768;

  /// <summary>Size of the pixel data section in bytes (160 bytes/row x 200 rows).</summary>
  internal const int PixelDataSize = 32000;

  /// <summary>Size of the scanline control byte section.</summary>
  internal const int ScbSize = 200;

  /// <summary>Padding between SCB and palette to reach offset 32256.</summary>
  internal const int PaddingSize = 56;

  /// <summary>Size of the palette section in bytes (16 palettes x 16 entries x 2 bytes).</summary>
  internal const int PaletteSize = 512;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>Pixel data (32000 bytes, 4bpp packed, 2 pixels per byte, 160 bytes per row).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Scanline control bytes (200 bytes, 1 per scanline, low nibble selects palette 0-15).</summary>
  public byte[] ScanlineControl { get; init; } = [];

  /// <summary>Palette data (512 bytes, 16 palettes x 16 entries x 2 bytes, 0RGB 4 bits each).</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>Converts this Apple IIgs SHR image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(AppleShrFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = FixedWidth;
    const int height = FixedHeight;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y) {
      var paletteIndex = file.ScanlineControl[y] & 0x0F;
      var paletteOffset = paletteIndex * 16 * 2;
      var rowOffset = y * 160;

      for (var x = 0; x < width; ++x) {
        var byteIndex = rowOffset + x / 2;
        int colorIndex;
        if ((x & 1) == 0)
          colorIndex = (file.PixelData[byteIndex] >> 4) & 0x0F;
        else
          colorIndex = file.PixelData[byteIndex] & 0x0F;

        var entryOffset = paletteOffset + colorIndex * 2;
        var entry = file.Palette[entryOffset] | (file.Palette[entryOffset + 1] << 8);
        var r = (byte)(((entry >> 8) & 0x0F) * 17);
        var g = (byte)(((entry >> 4) & 0x0F) * 17);
        var b = (byte)((entry & 0x0F) * 17);

        var pixelOffset = (y * width + x) * 3;
        rgb[pixelOffset] = r;
        rgb[pixelOffset + 1] = g;
        rgb[pixelOffset + 2] = b;
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. Apple IIgs SHR images have complex per-scanline palette constraints.</summary>
  public static AppleShrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to AppleShrFile is not supported due to per-scanline palette constraints.");
  }
}
