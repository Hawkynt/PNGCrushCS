using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ZxMultiArtist;

/// <summary>Attribute cell height mode for ZX Spectrum MultiArtist images.</summary>
public enum ZxMultiArtistMode {
  /// <summary>8x1 attribute cells (one attribute per scanline column). File size: 12288 bytes.</summary>
  Mg1 = 1,
  /// <summary>8x2 attribute cells. File size: 9216 bytes.</summary>
  Mg2 = 2,
  /// <summary>8x4 attribute cells. File size: 7680 bytes.</summary>
  Mg4 = 4,
  /// <summary>8x8 attribute cells (same as standard SCR). File size: 6912 bytes.</summary>
  Mg8 = 8,
}

/// <summary>In-memory representation of a ZX Spectrum MultiArtist image with variable attribute cell sizes.</summary>
public sealed class ZxMultiArtistFile : IImageFileFormat<ZxMultiArtistFile> {

  static string IImageFileFormat<ZxMultiArtistFile>.PrimaryExtension => ".mg1";
  static string[] IImageFileFormat<ZxMultiArtistFile>.FileExtensions => [".mg1", ".mg2", ".mg4", ".mg8"];
  static ZxMultiArtistFile IImageFileFormat<ZxMultiArtistFile>.FromFile(FileInfo file) => ZxMultiArtistReader.FromFile(file);
  static ZxMultiArtistFile IImageFileFormat<ZxMultiArtistFile>.FromBytes(byte[] data) => ZxMultiArtistReader.FromBytes(data);
  static ZxMultiArtistFile IImageFileFormat<ZxMultiArtistFile>.FromStream(Stream stream) => ZxMultiArtistReader.FromStream(stream);
  static byte[] IImageFileFormat<ZxMultiArtistFile>.ToBytes(ZxMultiArtistFile file) => ZxMultiArtistWriter.ToBytes(file);

  /// <summary>ZX Spectrum normal palette (bright=0): Black, Blue, Red, Magenta, Green, Cyan, Yellow, White.</summary>
  internal static readonly int[] NormalPalette = [
    0x000000, 0x0000CD, 0xCD0000, 0xCD00CD, 0x00CD00, 0x00CDCD, 0xCDCD00, 0xCDCDCD
  ];

  /// <summary>ZX Spectrum bright palette (bright=1).</summary>
  internal static readonly int[] BrightPalette = [
    0x000000, 0x0000FF, 0xFF0000, 0xFF00FF, 0x00FF00, 0x00FFFF, 0xFFFF00, 0xFFFFFF
  ];

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>The attribute cell height mode.</summary>
  public ZxMultiArtistMode Mode { get; init; } = ZxMultiArtistMode.Mg8;

  /// <summary>6144 bytes of 1bpp bitmap data in linear row order (deinterleaved).</summary>
  public byte[] BitmapData { get; init; } = [];

  /// <summary>Attribute data. Size depends on mode: MG1=6144, MG2=3072, MG4=1536, MG8=768.</summary>
  public byte[] AttributeData { get; init; } = [];

  /// <summary>Returns the attribute data size for a given mode.</summary>
  internal static int GetAttributeSize(ZxMultiArtistMode mode) => mode switch {
    ZxMultiArtistMode.Mg1 => 6144, // 32 * 192
    ZxMultiArtistMode.Mg2 => 3072, // 32 * 96
    ZxMultiArtistMode.Mg4 => 1536, // 32 * 48
    ZxMultiArtistMode.Mg8 => 768,  // 32 * 24
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid MultiArtist mode.")
  };

  /// <summary>Returns the total file size for a given mode.</summary>
  internal static int GetFileSize(ZxMultiArtistMode mode) => 6144 + GetAttributeSize(mode);

  /// <summary>Tries to detect the mode from a file size. Returns null if no mode matches.</summary>
  internal static ZxMultiArtistMode? DetectMode(int fileSize) => fileSize switch {
    12288 => ZxMultiArtistMode.Mg1,
    9216 => ZxMultiArtistMode.Mg2,
    7680 => ZxMultiArtistMode.Mg4,
    6912 => ZxMultiArtistMode.Mg8,
    _ => null
  };

  /// <summary>Converts this MultiArtist image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(ZxMultiArtistFile file) {
    ArgumentNullException.ThrowIfNull(file);

    const int width = 256;
    const int height = 192;
    var cellHeight = (int)file.Mode;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var bitValue = (file.BitmapData[byteIndex] >> bitPosition) & 1;

        var cellX = x / 8;
        var attrRow = y / cellHeight;
        var attrCols = 32;
        var attribute = file.AttributeData[attrRow * attrCols + cellX];
        var bright = (attribute >> 6) & 1;
        var paper = (attribute >> 3) & 0x07;
        var ink = attribute & 0x07;

        var palette = bright == 1 ? BrightPalette : NormalPalette;
        var color = palette[bitValue == 1 ? ink : paper];

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Not supported. ZX Spectrum images have complex attribute-based color constraints.</summary>
  public static ZxMultiArtistFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    throw new NotSupportedException("Conversion from RawImage to ZxMultiArtistFile is not supported due to complex attribute-based color constraints.");
  }
}
