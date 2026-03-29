using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DivGameMap;

/// <summary>In-memory representation of a DIV Games Studio FPG image (first entry).</summary>
public sealed class DivGameMapFile : IImageFileFormat<DivGameMapFile> {

  static string IImageFileFormat<DivGameMapFile>.PrimaryExtension => ".fpg";
  static string[] IImageFileFormat<DivGameMapFile>.FileExtensions => [".fpg"];
  static FormatCapability IImageFileFormat<DivGameMapFile>.Capabilities => FormatCapability.IndexedOnly;
  static DivGameMapFile IImageFileFormat<DivGameMapFile>.FromFile(FileInfo file) => DivGameMapReader.FromFile(file);
  static DivGameMapFile IImageFileFormat<DivGameMapFile>.FromBytes(byte[] data) => DivGameMapReader.FromBytes(data);
  static DivGameMapFile IImageFileFormat<DivGameMapFile>.FromStream(Stream stream) => DivGameMapReader.FromStream(stream);
  static byte[] IImageFileFormat<DivGameMapFile>.ToBytes(DivGameMapFile file) => DivGameMapWriter.ToBytes(file);

  /// <summary>Magic bytes: "fpg\x1A" (0x66 0x70 0x67 0x1A).</summary>
  internal static readonly byte[] Magic = [0x66, 0x70, 0x67, 0x1A];

  /// <summary>Size of the magic header in bytes.</summary>
  internal const int MagicSize = 4;

  /// <summary>Size of the global palette in bytes (256 RGB triplets).</summary>
  internal const int PaletteSize = 768;

  /// <summary>Minimum valid file size: magic(4) + palette(768).</summary>
  public const int MinFileSize = MagicSize + PaletteSize;

  /// <summary>Size of the entry header: code(4) + length(4) + description(32) + filename(12) + width(4) + height(4) + numPoints(4) = 64 bytes.</summary>
  internal const int EntryHeaderSize = 64;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>8-bit indexed pixel data (width * height bytes).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Global palette (768 bytes, 256 RGB triplets).</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>Converts this FPG image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(DivGameMapFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rgb = new byte[file.Width * file.Height * 3];
    for (var i = 0; i < file.PixelData.Length; ++i) {
      var index = file.PixelData[i];
      var palOffset = index * 3;
      var outOffset = i * 3;
      rgb[outOffset] = file.Palette[palOffset];
      rgb[outOffset + 1] = file.Palette[palOffset + 1];
      rgb[outOffset + 2] = file.Palette[palOffset + 2];
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. FPG files have complex multi-entry structure.</summary>
  public static DivGameMapFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to DivGameMapFile is not supported.");
  }
}
