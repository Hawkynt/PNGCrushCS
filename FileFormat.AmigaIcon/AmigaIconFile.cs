using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AmigaIcon;

/// <summary>In-memory representation of an Amiga Workbench icon (.info) file.</summary>
[FormatMagicBytes([0xE3, 0x10])]
public sealed class AmigaIconFile : IImageFileFormat<AmigaIconFile> {

  static string IImageFileFormat<AmigaIconFile>.PrimaryExtension => ".info";
  static string[] IImageFileFormat<AmigaIconFile>.FileExtensions => [".info"];
  static AmigaIconFile IImageFileFormat<AmigaIconFile>.FromFile(FileInfo file) => AmigaIconReader.FromFile(file);
  static AmigaIconFile IImageFileFormat<AmigaIconFile>.FromBytes(byte[] data) => AmigaIconReader.FromBytes(data);
  static AmigaIconFile IImageFileFormat<AmigaIconFile>.FromStream(Stream stream) => AmigaIconReader.FromStream(stream);
  static RawImage IImageFileFormat<AmigaIconFile>.ToRawImage(AmigaIconFile file) => file.ToRawImage();
  static AmigaIconFile IImageFileFormat<AmigaIconFile>.FromRawImage(RawImage image) => FromRawImage(image);
  static byte[] IImageFileFormat<AmigaIconFile>.ToBytes(AmigaIconFile file) => AmigaIconWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of bitplanes (1..8).</summary>
  public int Depth { get; init; }

  /// <summary>Icon type (1=Disk, 2=Drawer, 3=Tool, etc.).</summary>
  public int IconType { get; init; } = (int)AmigaIconType.Tool;

  /// <summary>Raw planar bitmap data for the first image. Non-interleaved: all rows of plane 0, then plane 1, etc. Word-aligned rows.</summary>
  public byte[] PlanarData { get; init; } = [];

  /// <summary>Optional palette as RGB triplets (3 bytes per entry). Defaults to the Workbench 4-color palette when null.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Raw 78-byte DiskObject header preserved for round-trip fidelity. When null, a fresh header is constructed on write.</summary>
  public byte[]? RawHeader { get; init; }

  /// <summary>Default Amiga Workbench 4-color palette (depth &lt;= 2): gray, black, white, blue.</summary>
  public static readonly byte[] DefaultPalette = [
    0x95, 0x95, 0x95, // index 0: gray
    0x00, 0x00, 0x00, // index 1: black
    0xFF, 0xFF, 0xFF, // index 2: white
    0x3B, 0x67, 0xA2, // index 3: blue
  ];

  /// <summary>Computes the number of bytes per plane row (word-aligned).</summary>
  internal static int BytesPerPlaneRow(int width) => ((width + 15) / 16) * 2;

  /// <summary>Computes the total expected planar data size for one image.</summary>
  internal static int PlanarDataSize(int width, int height, int depth) => BytesPerPlaneRow(width) * height * depth;

  public RawImage ToRawImage() {
    var palette = this.Palette ?? _GetDefaultPaletteForDepth(this.Depth);
    var paletteCount = palette.Length / 3;
    var chunky = _PlanarToChunky(this.PlanarData, this.Width, this.Height, this.Depth);

    return new() {
      Width = this.Width,
      Height = this.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = palette[..],
      PaletteCount = paletteCount,
    };
  }

  public static AmigaIconFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed8.", nameof(image));
    if (image.Palette == null)
      throw new ArgumentException("RawImage must have a palette.", nameof(image));

    var maxIndex = _FindMaxIndex(image.PixelData);
    var depth = _BitsNeeded(maxIndex);
    if (depth < 1)
      depth = 1;

    var planar = _ChunkyToPlanar(image.PixelData, image.Width, image.Height, depth);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Depth = depth,
      PlanarData = planar,
      Palette = image.Palette[..],
    };
  }

  private static byte[] _GetDefaultPaletteForDepth(int depth) {
    var colors = 1 << depth;
    if (colors <= 4)
      return DefaultPalette[..];

    // For deeper images, extend with black entries
    var palette = new byte[colors * 3];
    DefaultPalette.AsSpan(0, Math.Min(DefaultPalette.Length, palette.Length)).CopyTo(palette);
    return palette;
  }

  /// <summary>
  ///   Converts non-interleaved planar data (word-aligned rows) to chunky (one byte per pixel).
  ///   Layout: all rows of plane 0, then all rows of plane 1, etc.
  /// </summary>
  internal static byte[] _PlanarToChunky(ReadOnlySpan<byte> planarData, int width, int height, int depth) {
    var bytesPerPlaneRow = BytesPerPlaneRow(width);
    var bytesPerPlane = bytesPerPlaneRow * height;
    var result = new byte[width * height];

    for (var plane = 0; plane < depth; ++plane) {
      var planeOffset = plane * bytesPerPlane;

      for (var y = 0; y < height; ++y) {
        var rowOffset = planeOffset + y * bytesPerPlaneRow;

        for (var x = 0; x < width; ++x) {
          var byteIndex = x / 8;
          var bitIndex = 7 - (x % 8);
          var dataOffset = rowOffset + byteIndex;

          if (dataOffset < planarData.Length && (planarData[dataOffset] & (1 << bitIndex)) != 0)
            result[y * width + x] |= (byte)(1 << plane);
        }
      }
    }

    return result;
  }

  /// <summary>Converts chunky pixel data to non-interleaved planar format with word-aligned rows.</summary>
  internal static byte[] _ChunkyToPlanar(ReadOnlySpan<byte> chunkyData, int width, int height, int depth) {
    var bytesPerPlaneRow = BytesPerPlaneRow(width);
    var bytesPerPlane = bytesPerPlaneRow * height;
    var result = new byte[bytesPerPlane * depth];

    for (var plane = 0; plane < depth; ++plane) {
      var planeOffset = plane * bytesPerPlane;

      for (var y = 0; y < height; ++y) {
        var rowOffset = planeOffset + y * bytesPerPlaneRow;

        for (var x = 0; x < width; ++x) {
          var pixel = chunkyData[y * width + x];
          if ((pixel & (1 << plane)) != 0) {
            var byteIndex = x / 8;
            var bitIndex = 7 - (x % 8);
            result[rowOffset + byteIndex] |= (byte)(1 << bitIndex);
          }
        }
      }
    }

    return result;
  }

  private static int _FindMaxIndex(ReadOnlySpan<byte> data) {
    var max = 0;
    foreach (var b in data)
      if (b > max)
        max = b;

    return max;
  }

  private static int _BitsNeeded(int maxValue) {
    if (maxValue <= 0)
      return 1;

    var bits = 0;
    var v = maxValue;
    while (v > 0) {
      v >>= 1;
      ++bits;
    }

    return bits;
  }
}
