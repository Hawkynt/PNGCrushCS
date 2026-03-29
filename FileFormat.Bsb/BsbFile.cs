using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Bsb;

/// <summary>In-memory representation of a BSB/KAP nautical chart image.</summary>
public sealed class BsbFile : IImageFileFormat<BsbFile> {

  static string IImageFileFormat<BsbFile>.PrimaryExtension => ".kap";
  static string[] IImageFileFormat<BsbFile>.FileExtensions => [".kap", ".bsb"];
  static FormatCapability IImageFileFormat<BsbFile>.Capabilities => FormatCapability.IndexedOnly;
  static BsbFile IImageFileFormat<BsbFile>.FromFile(FileInfo file) => BsbReader.FromFile(file);
  static BsbFile IImageFileFormat<BsbFile>.FromBytes(byte[] data) => BsbReader.FromBytes(data);
  static BsbFile IImageFileFormat<BsbFile>.FromStream(Stream stream) => BsbReader.FromStream(stream);
  static byte[] IImageFileFormat<BsbFile>.ToBytes(BsbFile file) => BsbWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Pixel data (width * height bytes of 8-bit palette indices).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>RGB palette (3 bytes per entry: R, G, B).</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>Number of entries in the palette.</summary>
  public int PaletteCount { get; init; }

  /// <summary>Bits per pixel for palette index encoding (1-7, typically 7).</summary>
  public int Depth { get; init; } = 7;

  /// <summary>Chart name from the BSB header.</summary>
  public string Name { get; init; } = "";

  /// <summary>Converts a BSB file to a <see cref="RawImage"/> with Indexed8 format.</summary>
  public static RawImage ToRawImage(BsbFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var paletteBytes = new byte[file.PaletteCount * 3];
    file.Palette.AsSpan(0, Math.Min(file.Palette.Length, paletteBytes.Length)).CopyTo(paletteBytes.AsSpan(0));

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = paletteBytes,
      PaletteCount = file.PaletteCount,
    };
  }

  /// <summary>Creates a BSB file from a <see cref="RawImage"/>. Must be Indexed8 format.</summary>
  public static BsbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"BSB requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Palette == null || image.PaletteCount <= 0)
      throw new ArgumentException("BSB requires a palette.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette[..],
      PaletteCount = image.PaletteCount,
      Depth = 7,
      Name = "NOAA",
    };
  }
}
