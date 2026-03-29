using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PcPaint;

/// <summary>In-memory representation of a PC Paint/Pictor Page Format image.</summary>
public sealed class PcPaintFile : IImageFileFormat<PcPaintFile> {

  /// <summary>Magic number identifying PC Paint/Pictor files (0x1234, stored as 34 12 in LE).</summary>
  internal const ushort Magic = 0x1234;

  /// <summary>Size of the file header in bytes.</summary>
  internal const int HeaderSize = 18;

  /// <summary>Size of a 256-entry RGB palette in bytes (256 entries x 3 bytes).</summary>
  internal const int PaletteSize = 768;

  static string IImageFileFormat<PcPaintFile>.PrimaryExtension => ".pic";
  static string[] IImageFileFormat<PcPaintFile>.FileExtensions => [".pic", ".clp"];
  static FormatCapability IImageFileFormat<PcPaintFile>.Capabilities => FormatCapability.IndexedOnly;
  static PcPaintFile IImageFileFormat<PcPaintFile>.FromFile(FileInfo file) => PcPaintReader.FromFile(file);
  static PcPaintFile IImageFileFormat<PcPaintFile>.FromBytes(byte[] data) => PcPaintReader.FromBytes(data);
  static PcPaintFile IImageFileFormat<PcPaintFile>.FromStream(Stream stream) => PcPaintReader.FromStream(stream);
  static byte[] IImageFileFormat<PcPaintFile>.ToBytes(PcPaintFile file) => PcPaintWriter.ToBytes(file);

  static bool? IImageFileFormat<PcPaintFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 12 || header[0] != 0x34 || header[1] != 0x12)
      return null;
    var w = (ushort)(header[2] | (header[3] << 8));
    var h = (ushort)(header[4] | (header[5] << 8));
    var planes = header[10];
    var bpp = header[11];
    return w > 0 && h > 0 && planes >= 1 && planes <= 4 && bpp is 1 or 2 or 4 or 8 ? true : null;
  }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Horizontal offset.</summary>
  public ushort XOffset { get; init; }

  /// <summary>Vertical offset.</summary>
  public ushort YOffset { get; init; }

  /// <summary>Number of bit planes (1-4).</summary>
  public byte Planes { get; init; } = 1;

  /// <summary>Bits per pixel per plane (1, 2, 4, or 8).</summary>
  public byte BitsPerPixel { get; init; } = 8;

  /// <summary>Horizontal aspect ratio.</summary>
  public ushort XAspect { get; init; }

  /// <summary>Vertical aspect ratio.</summary>
  public ushort YAspect { get; init; }

  /// <summary>Palette data (RGB triplets, 3 bytes per entry).</summary>
  public byte[] Palette { get; init; } = [];

  /// <summary>Pixel data (width * height bytes of 8-bit palette indices).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts a PC Paint file to a <see cref="RawImage"/> with Indexed8 format.</summary>
  public static RawImage ToRawImage(PcPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = file.Palette.Length / 3,
    };
  }

  /// <summary>Creates a PC Paint file from a <see cref="RawImage"/>. Must be Indexed8.</summary>
  public static PcPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"PC Paint requires Indexed8 pixel format, got {image.Format}.", nameof(image));
    if (image.Palette == null || image.Palette.Length < 3)
      throw new ArgumentException("PC Paint requires an RGB palette.", nameof(image));

    var palette = new byte[PaletteSize];
    image.Palette.AsSpan(0, Math.Min(image.Palette.Length, PaletteSize)).CopyTo(palette);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = palette,
      PixelData = image.PixelData[..],
    };
  }
}
