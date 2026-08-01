using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>Data model representing a PNG file</summary>
[FormatMagicBytes([0x89, 0x50, 0x4E, 0x47])]
[FormatMimeType("image/png", "image/x-png")]
public readonly record struct PngFile :
  IImageFormatReader<PngFile>, IImageToRawImage<PngFile>, IImageFromRawImage<PngFile>, IImageFormatWriter<PngFile>,
  IFormatChunkLayout<PngFile>, IFormatChunkRewriter<PngFile>, IFormatChunkPlanRewriter<PngFile> {

  static string IImageFormatMetadata<PngFile>.PrimaryExtension => ".png";
  static string[] IImageFormatMetadata<PngFile>.FileExtensions => [".png"];
  static PngFile IImageFormatReader<PngFile>.FromSpan(ReadOnlySpan<byte> data) => PngReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PngFile>.Capabilities => FormatCapability.HasDedicatedOptimizer;
  static VideoMode[] IImageFormatMetadata<PngFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)])
  ];
  static byte[] IImageFormatWriter<PngFile>.ToBytes(PngFile file) => PngWriter.ToBytes(file);

  static IEnumerable<ChunkSpan> IFormatChunkLayout<PngFile>.EnumerateChunks(ReadOnlySpan<byte> data)
    => PngChunkLayout.Enumerate(data);

  static byte[] IFormatChunkRewriter<PngFile>.Rewrite(ReadOnlySpan<byte> data, IReadOnlyList<ChunkRewriteRule> rules)
    => PngChunkLayout.Rewrite(data, rules);

  static ChunkRewriteResult IFormatChunkPlanRewriter<PngFile>.ApplyPlan(ReadOnlySpan<byte> data, ChunkRewritePlan plan)
    => PngChunkLayout.ApplyPlan(data, plan);
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

    if (file.ColorType == PngColorType.Grayscale && file.BitDepth < 8)
      pixelData = _UnpackGray(pixelData, file.Width, file.Height, file.BitDepth);
    // For indexed with BitDepth 2, unpack to 8-bit indices
    else if (file.ColorType == PngColorType.Palette && file.BitDepth == 2)
      pixelData = _Unpack2BitTo8Bit(pixelData, file.Width, file.Height);
    else if (format is PixelFormat.Indexed1 or PixelFormat.Indexed4)
      pixelData = _RemoveRowPadding(pixelData, file.Width, file.Height, file.BitDepth);

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

    image = image.EnsureAnyFormat(
      PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Rgba64, PixelFormat.Rgb48,
      PixelFormat.GrayAlpha16, PixelFormat.Gray16, PixelFormat.Gray8,
      PixelFormat.Indexed8, PixelFormat.Indexed4, PixelFormat.Indexed1);

    var (colorType, bitDepth) = _GetPngSettings(image.Format);
    var stride = _CalculateStride(image.Width, image.Format, bitDepth);
    var pixelData = image.Format is PixelFormat.Indexed1 or PixelFormat.Indexed4
      ? _AddRowPadding(image.PixelData, image.Width, image.Height, bitDepth)
      : image.PixelData;
    var rows = _SplitIntoRows(pixelData, stride, image.Height);

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

  /// <summary>Spreads packed grey samples to a byte each, widening each to the full range.</summary>
  /// <remarks>
  /// The widening repeats the sample's bits rather than scaling it — one bit becomes 0 or 255, two
  /// bits step 0/85/170/255, four bits step by 17. Multiplying by 255 and dividing instead leaves
  /// the top of the range short, which is the difference between white and nearly white.
  /// </remarks>
  private static byte[] _UnpackGray(byte[] packed, int width, int height, int bitDepth) {
    var stride = (width * bitDepth + 7) / 8;
    var pixels = new byte[width * height];
    var perByte = 8 / bitDepth;
    var mask = (1 << bitDepth) - 1;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = y * stride + x / perByte;
      if (at >= packed.Length)
        break;

      var shift = 8 - bitDepth - (x % perByte) * bitDepth;
      var value = (packed[at] >> shift) & mask;

      pixels[y * width + x] = bitDepth switch {
        1 => value != 0 ? (byte)255 : (byte)0,
        2 => ChannelScaling.Expand2(value),
        _ => ChannelScaling.Expand4(value),
      };
    }

    return pixels;
  }

  private static PixelFormat _GetPixelFormat(PngColorType colorType, int bitDepth) => colorType switch {
    // Grey below a byte is widened on the way out rather than carried packed: there is no packed
    // grey format here, and a bilevel PNG is common enough that refusing it is not an option.
    PngColorType.Grayscale when bitDepth is 1 or 2 or 4 or 8 => PixelFormat.Gray8,
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

  /// <summary>
  /// Restacks sub-byte rows from the byte-aligned layout PNG stores them in to the continuous one
  /// <see cref="RawImage"/> uses.
  /// </summary>
  /// <remarks>
  /// PNG starts every row on a byte boundary; a <see cref="RawImage"/> in a sub-byte format runs
  /// its indices straight on across the whole picture, which is what <see cref="PixelConverter"/>
  /// and <see cref="ColorQuantizer.PackIndices"/> both expect. The two agree for any width that is
  /// a multiple of eight pixels — which is nearly every picture, and why this went unnoticed — and
  /// diverge by the padding bits for the rest, throwing every row after the first out of step.
  /// </remarks>
  private static byte[] _RemoveRowPadding(byte[] padded, int width, int height, int bitsPerPixel) {
    var paddedStride = (width * bitsPerPixel + 7) / 8;
    if (paddedStride * 8 == width * bitsPerPixel)
      return padded;

    var result = new byte[(width * height * bitsPerPixel + 7) / 8];
    var mask = (1 << bitsPerPixel) - 1;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var sourceBit = y * paddedStride * 8 + x * bitsPerPixel;
      var sourceByte = sourceBit >> 3;
      if (sourceByte >= padded.Length)
        return result;

      var value = (padded[sourceByte] >> (8 - bitsPerPixel - (sourceBit & 7))) & mask;
      var targetBit = (y * width + x) * bitsPerPixel;
      result[targetBit >> 3] |= (byte)(value << (8 - bitsPerPixel - (targetBit & 7)));
    }

    return result;
  }

  /// <summary>Restacks continuous sub-byte rows into the byte-aligned layout PNG stores.</summary>
  private static byte[] _AddRowPadding(byte[] continuous, int width, int height, int bitsPerPixel) {
    var paddedStride = (width * bitsPerPixel + 7) / 8;
    if (paddedStride * 8 == width * bitsPerPixel)
      return continuous;

    var result = new byte[paddedStride * height];
    var mask = (1 << bitsPerPixel) - 1;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var sourceBit = (y * width + x) * bitsPerPixel;
      var sourceByte = sourceBit >> 3;
      if (sourceByte >= continuous.Length)
        return result;

      var value = (continuous[sourceByte] >> (8 - bitsPerPixel - (sourceBit & 7))) & mask;
      var targetBit = y * paddedStride * 8 + x * bitsPerPixel;
      result[targetBit >> 3] |= (byte)(value << (8 - bitsPerPixel - (targetBit & 7)));
    }

    return result;
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
