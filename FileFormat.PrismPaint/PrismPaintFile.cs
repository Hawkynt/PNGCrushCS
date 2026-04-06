using System;
using FileFormat.Core;

namespace FileFormat.PrismPaint;

/// <summary>In-memory representation of an Atari Falcon Prism Paint (.pnt/.tpi) indexed image.</summary>
public readonly record struct PrismPaintFile : IImageFormatReader<PrismPaintFile>, IImageToRawImage<PrismPaintFile>, IImageFromRawImage<PrismPaintFile>, IImageFormatWriter<PrismPaintFile> {

  /// <summary>Size of the dimension header (width u16 LE + height u16 LE).</summary>
  public const int HeaderSize = 4;

  /// <summary>Number of palette entries.</summary>
  public const int PaletteEntryCount = 256;

  /// <summary>Bytes per palette entry in the Falcon format (RRRRRRrr GGGGGGgg 00000000 BBBBBBbb).</summary>
  public const int BytesPerPaletteEntry = 4;

  /// <summary>Size of the raw palette section in bytes.</summary>
  public const int PaletteDataSize = PaletteEntryCount * BytesPerPaletteEntry;

  /// <summary>Minimum valid file size (header + palette + at least 1 pixel).</summary>
  public const int MinFileSize = HeaderSize + PaletteDataSize + 1;

  static string IImageFormatMetadata<PrismPaintFile>.PrimaryExtension => ".pnt";
  static string[] IImageFormatMetadata<PrismPaintFile>.FileExtensions => [".pnt", ".tpi"];
  static PrismPaintFile IImageFormatReader<PrismPaintFile>.FromSpan(ReadOnlySpan<byte> data) => PrismPaintReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PrismPaintFile>.Capabilities => FormatCapability.IndexedOnly;
  static byte[] IImageFormatWriter<PrismPaintFile>.ToBytes(PrismPaintFile file) => PrismPaintWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>RGB palette (3 bytes per entry, 768 bytes total).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Pixel data (1 byte per pixel, width x height bytes).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts the Falcon 4-byte palette entry to an RGB triplet.</summary>
  internal static void ConvertFalconPaletteToRgb(ReadOnlySpan<byte> falcon, Span<byte> rgb) {
    for (var i = 0; i < PaletteEntryCount; ++i) {
      var srcOff = i * BytesPerPaletteEntry;
      var dstOff = i * 3;
      rgb[dstOff] = falcon[srcOff];       // R
      rgb[dstOff + 1] = falcon[srcOff + 1]; // G
      rgb[dstOff + 2] = falcon[srcOff + 3]; // B
    }
  }

  /// <summary>Converts an RGB palette to the Falcon 4-byte palette format.</summary>
  internal static void ConvertRgbPaletteToFalcon(ReadOnlySpan<byte> rgb, Span<byte> falcon) {
    for (var i = 0; i < PaletteEntryCount; ++i) {
      var srcOff = i * 3;
      var dstOff = i * BytesPerPaletteEntry;
      falcon[dstOff] = rgb[srcOff];       // R
      falcon[dstOff + 1] = rgb[srcOff + 1]; // G
      falcon[dstOff + 2] = 0x00;            // padding
      falcon[dstOff + 3] = rgb[srcOff + 2]; // B
    }
  }

  public static RawImage ToRawImage(PrismPaintFile file) {

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = file.Palette[..],
      PaletteCount = PaletteEntryCount,
    };
  }

  public static PrismPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentException($"Dimensions must be positive, got {image.Width}x{image.Height}.", nameof(image));
    if (image.Width > 65535 || image.Height > 65535)
      throw new ArgumentException($"Dimensions must fit in a ushort, got {image.Width}x{image.Height}.", nameof(image));
    if (image.Palette == null || image.Palette.Length < 3)
      throw new ArgumentException("Prism Paint requires an RGB palette.", nameof(image));

    var palette = new byte[PaletteEntryCount * 3];
    image.Palette.AsSpan(0, Math.Min(image.Palette.Length, palette.Length)).CopyTo(palette);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Palette = palette,
      PixelData = image.PixelData[..],
    };
  }
}
