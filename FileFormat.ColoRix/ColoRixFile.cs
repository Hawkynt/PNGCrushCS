using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.ColoRix;

/// <summary>In-memory representation of a ColoRIX VGA paint image.</summary>
[FormatMagicBytes([0x52, 0x49, 0x58, 0x33])]
public sealed class ColoRixFile : IImageFileFormat<ColoRixFile> {

  /// <summary>The VGA palette type marker (0xAF).</summary>
  internal const byte VgaPaletteType = 0xAF;

  /// <summary>Size of a VGA palette in bytes (256 entries x 3 bytes).</summary>
  internal const int PaletteSize = 768;

  /// <summary>Size of the file header in bytes.</summary>
  internal const int HeaderSize = 10;

  static string IImageFileFormat<ColoRixFile>.PrimaryExtension => ".rix";
  static string[] IImageFileFormat<ColoRixFile>.FileExtensions => [".rix", ".scx", ".sci"];
  static FormatCapability IImageFileFormat<ColoRixFile>.Capabilities => FormatCapability.IndexedOnly;
  static ColoRixFile IImageFileFormat<ColoRixFile>.FromFile(FileInfo file) => ColoRixReader.FromFile(file);
  static ColoRixFile IImageFileFormat<ColoRixFile>.FromBytes(byte[] data) => ColoRixReader.FromBytes(data);
  static ColoRixFile IImageFileFormat<ColoRixFile>.FromStream(Stream stream) => ColoRixReader.FromStream(stream);
  static byte[] IImageFileFormat<ColoRixFile>.ToBytes(ColoRixFile file) => ColoRixWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>VGA palette (768 bytes: 256 entries x 3 bytes, 6-bit values 0-63).</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>Pixel data (width * height bytes of 8-bit palette indices).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Storage type (uncompressed or RLE).</summary>
  public ColoRixCompression StorageType { get; init; }

  /// <summary>Converts a ColoRIX file to a <see cref="RawImage"/> with Indexed8 format and 8-bit expanded palette.</summary>
  public static RawImage ToRawImage(ColoRixFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var palette8Bit = new byte[PaletteSize];
    for (var i = 0; i < PaletteSize; ++i)
      palette8Bit[i] = (byte)((file.Palette[i] & 0x3F) * 255 / 63);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette8Bit,
      PaletteCount = 256,
    };
  }

  /// <summary>Creates a ColoRIX file from a <see cref="RawImage"/>. Must be Indexed8 with a 256-entry palette.</summary>
  public static ColoRixFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"ColoRIX requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Palette == null || image.Palette.Length < PaletteSize)
      throw new ArgumentException("ColoRIX requires a 256-entry RGB palette.", nameof(image));

    var palette6Bit = new byte[PaletteSize];
    for (var i = 0; i < PaletteSize; ++i)
      palette6Bit[i] = (byte)(image.Palette[i] * 63 / 255);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Palette = palette6Bit,
      PixelData = image.PixelData[..],
      StorageType = ColoRixCompression.None,
    };
  }
}
