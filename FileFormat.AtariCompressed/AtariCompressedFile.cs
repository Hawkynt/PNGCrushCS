using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AtariCompressed;

/// <summary>In-memory representation of an Atari Compressed Screen (.acr) with RLE encoding.</summary>
public sealed class AtariCompressedFile : IImageFileFormat<AtariCompressedFile> {

  /// <summary>Default decompressed screen size: 40 bytes/row x 192 rows (Graphics 8, 1bpp).</summary>
  internal const int DecompressedSize = 7680;

  /// <summary>Default width in pixels (320 for 1bpp mode).</summary>
  internal const int DefaultWidth = 320;

  /// <summary>Default height in pixels.</summary>
  internal const int DefaultHeight = 192;

  /// <summary>Bytes per row in the raw screen dump.</summary>
  internal const int BytesPerRow = 40;

  static string IImageFileFormat<AtariCompressedFile>.PrimaryExtension => ".acr";
  static string[] IImageFileFormat<AtariCompressedFile>.FileExtensions => [".acr", ".acp"];
  static FormatCapability IImageFileFormat<AtariCompressedFile>.Capabilities => FormatCapability.IndexedOnly;
  static AtariCompressedFile IImageFileFormat<AtariCompressedFile>.FromFile(FileInfo file) => AtariCompressedReader.FromFile(file);
  static AtariCompressedFile IImageFileFormat<AtariCompressedFile>.FromBytes(byte[] data) => AtariCompressedReader.FromBytes(data);
  static AtariCompressedFile IImageFileFormat<AtariCompressedFile>.FromStream(Stream stream) => AtariCompressedReader.FromStream(stream);
  static byte[] IImageFileFormat<AtariCompressedFile>.ToBytes(AtariCompressedFile file) => AtariCompressedWriter.ToBytes(file);

  /// <summary>Width in pixels.</summary>
  public int Width { get; init; } = DefaultWidth;

  /// <summary>Height in pixels.</summary>
  public int Height { get; init; } = DefaultHeight;

  /// <summary>Raw decompressed screen data (7680 bytes for default 320x192 1bpp).</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts this Atari Compressed Screen to an Indexed1 raw image (320x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(AtariCompressedFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var rowStride = file.Width / 8;
    var pixelData = new byte[rowStride * file.Height];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, pixelData.Length)).CopyTo(pixelData);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates an Atari Compressed Screen from an Indexed1 raw image (320x192).</summary>
  public static AtariCompressedFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != DefaultWidth || image.Height != DefaultHeight)
      throw new ArgumentException($"Expected {DefaultWidth}x{DefaultHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[DecompressedSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, DecompressedSize)).CopyTo(pixelData);

    return new() { PixelData = pixelData };
  }
}
