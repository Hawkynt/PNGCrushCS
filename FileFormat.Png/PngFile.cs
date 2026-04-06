using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>Data model representing a PNG file</summary>
[FormatMagicBytes([0x89, 0x50, 0x4E, 0x47])]
public readonly record struct PngFile : IImageFormatReader<PngFile>, IImageToRawImage<PngFile>, IImageFromRawImage<PngFile>, IImageFormatWriter<PngFile> {

  static string IImageFormatMetadata<PngFile>.PrimaryExtension => ".png";
  static string[] IImageFormatMetadata<PngFile>.FileExtensions => [".png"];
  static PngFile IImageFormatReader<PngFile>.FromSpan(ReadOnlySpan<byte> data) => PngReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PngFile>.Capabilities => FormatCapability.HasDedicatedOptimizer;
  static byte[] IImageFormatWriter<PngFile>.ToBytes(PngFile file) => PngWriter.ToBytes(file);
  /// <summary>Image width in pixels</summary>
  public required int Width { get; init; }

  /// <summary>Image height in pixels</summary>
  public required int Height { get; init; }

  /// <summary>Bit depth per channel (1, 2, 4, 8, or 16)</summary>
  public required int BitDepth { get; init; }

  /// <summary>PNG color type</summary>
  public required PngColorType ColorType { get; init; }

  /// <summary>Interlace method</summary>
  public PngInterlaceMethod InterlaceMethod { get; init; }

  /// <summary>Raw pixel data as scanlines (one byte array per row, without filter bytes)</summary>
  public byte[][]? PixelData { get; init; }

  /// <summary>Palette data (RGB triplets, 3 bytes per entry)</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Number of actual palette entries used</summary>
  public int PaletteCount { get; init; }

  /// <summary>Transparency chunk data (tRNS)</summary>
  public byte[]? Transparency { get; init; }

  /// <summary>Ancillary chunks to preserve before PLTE</summary>
  public IReadOnlyList<PngChunk>? ChunksBeforePlte { get; init; }

  /// <summary>Ancillary chunks to preserve between PLTE and IDAT</summary>
  public IReadOnlyList<PngChunk>? ChunksBetweenPlteAndIdat { get; init; }

  /// <summary>Ancillary chunks to preserve after IDAT</summary>
  public IReadOnlyList<PngChunk>? ChunksAfterIdat { get; init; }

  public static RawImage ToRawImage(PngFile file) {
    if (file.PixelData == null)
      throw new ArgumentException("PixelData must not be null.", nameof(file));

    var format = _GetPixelFormat(file.ColorType, file.BitDepth);
    var pixelData = _FlattenRows(file.PixelData);

    // For indexed with BitDepth 2, unpack to 8-bit indices
    if (file.ColorType == PngColorType.Palette && file.BitDepth == 2)
      pixelData = _Unpack2BitTo8Bit(pixelData, file.Width, file.Height);

    byte[]? palette = null;
    var paletteCount = 0;
    byte[]? alphaTable = null;

    if (file.ColorType == PngColorType.Palette) {
      palette = file.Palette != null ? file.Palette[..] : null;
      paletteCount = file.PaletteCount;
      if (file.Transparency != null)
        alphaTable = file.Transparency[..];
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = pixelData,
      Palette = palette,
      PaletteCount = paletteCount,
      AlphaTable = alphaTable,
    };
  }

  public static PngFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var (colorType, bitDepth) = _GetPngSettings(image.Format);
    var stride = _CalculateStride(image.Width, image.Format, bitDepth);
    var rows = _SplitIntoRows(image.PixelData, stride, image.Height);

    byte[]? palette = null;
    var paletteCount = 0;
    byte[]? transparency = null;

    if (colorType == PngColorType.Palette) {
      palette = image.Palette != null ? image.Palette[..] : null;
      paletteCount = image.PaletteCount;
      if (image.AlphaTable != null)
        transparency = image.AlphaTable[..];
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      BitDepth = bitDepth,
      ColorType = colorType,
      PixelData = rows,
      Palette = palette,
      PaletteCount = paletteCount,
      Transparency = transparency,
    };
  }

  private static PixelFormat _GetPixelFormat(PngColorType colorType, int bitDepth) => colorType switch {
    PngColorType.Grayscale when bitDepth == 8 => PixelFormat.Gray8,
    PngColorType.Grayscale when bitDepth == 16 => PixelFormat.Gray16,
    PngColorType.GrayscaleAlpha when bitDepth == 8 => PixelFormat.GrayAlpha16,
    PngColorType.RGB when bitDepth == 8 => PixelFormat.Rgb24,
    PngColorType.RGB when bitDepth == 16 => PixelFormat.Rgb48,
    PngColorType.RGBA when bitDepth == 8 => PixelFormat.Rgba32,
    PngColorType.RGBA when bitDepth == 16 => PixelFormat.Rgba64,
    PngColorType.Palette when bitDepth == 1 => PixelFormat.Indexed1,
    PngColorType.Palette when bitDepth == 4 => PixelFormat.Indexed4,
    PngColorType.Palette when bitDepth is 2 or 8 => PixelFormat.Indexed8,
    _ => throw new ArgumentException($"Unsupported PNG color type {colorType} with bit depth {bitDepth}.")
  };

  private static (PngColorType colorType, int bitDepth) _GetPngSettings(PixelFormat format) => format switch {
    PixelFormat.Gray8 => (PngColorType.Grayscale, 8),
    PixelFormat.Gray16 => (PngColorType.Grayscale, 16),
    PixelFormat.GrayAlpha16 => (PngColorType.GrayscaleAlpha, 8),
    PixelFormat.Rgb24 => (PngColorType.RGB, 8),
    PixelFormat.Rgb48 => (PngColorType.RGB, 16),
    PixelFormat.Rgba32 => (PngColorType.RGBA, 8),
    PixelFormat.Rgba64 => (PngColorType.RGBA, 16),
    PixelFormat.Indexed8 => (PngColorType.Palette, 8),
    PixelFormat.Indexed4 => (PngColorType.Palette, 4),
    PixelFormat.Indexed1 => (PngColorType.Palette, 1),
    _ => throw new ArgumentException($"Unsupported pixel format for PNG: {format}.", nameof(format))
  };

  private static int _CalculateStride(int width, PixelFormat format, int bitDepth) {
    var bpp = RawImage.BitsPerPixel(format);
    return (width * bpp + 7) / 8;
  }

  private static byte[] _FlattenRows(byte[][] rows) {
    var totalLength = 0;
    foreach (var row in rows)
      totalLength += row.Length;

    var result = new byte[totalLength];
    var offset = 0;
    foreach (var row in rows) {
      row.AsSpan(0, row.Length).CopyTo(result.AsSpan(offset));
      offset += row.Length;
    }

    return result;
  }

  private static byte[][] _SplitIntoRows(byte[] data, int stride, int height) {
    var rows = new byte[height][];
    for (var y = 0; y < height; ++y) {
      rows[y] = new byte[stride];
      var sourceOffset = y * stride;
      var copyLength = Math.Min(stride, data.Length - sourceOffset);
      if (copyLength > 0)
        data.AsSpan(sourceOffset, copyLength).CopyTo(rows[y].AsSpan(0));
    }

    return rows;
  }

  private static byte[] _Unpack2BitTo8Bit(byte[] packed, int width, int height) {
    var result = new byte[width * height];
    var packedStride = (width * 2 + 7) / 8;
    for (var y = 0; y < height; ++y) {
      var rowOffset = y * packedStride;
      for (var x = 0; x < width; ++x) {
        var byteIndex = rowOffset + x / 4;
        var shift = 6 - (x % 4) * 2;
        result[y * width + x] = (byte)((packed[byteIndex] >> shift) & 0x03);
      }
    }

    return result;
  }
}
