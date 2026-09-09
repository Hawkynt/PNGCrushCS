using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Core.PixelFormats;
using RawRgba32 = FileFormat.Core.PixelFormats.Rgba32;

namespace FileFormat.Png;

/// <summary>Exact raw representations that PNG can serialize without pixel conversion.</summary>
public readonly partial record struct PngFile :
  IImageFromRawImage<PngFile, Gray8>,
  IImageFromRawImage<PngFile, Gray16>,
  IImageFromRawImage<PngFile, GrayAlpha16>,
  IImageFromRawImage<PngFile, GrayAlpha32>,
  IImageFromRawImage<PngFile, Rgb24>,
  IImageFromRawImage<PngFile, Rgb48>,
  IImageFromRawImage<PngFile, RawRgba32>,
  IImageFromRawImage<PngFile, Rgba64>,
  IImageFromRawImage<PngFile, Indexed1>,
  IImageFromRawImage<PngFile, Indexed2>,
  IImageFromRawImage<PngFile, Indexed4>,
  IImageFromRawImage<PngFile, Indexed8> {

  public static PngFile FromRawImage(RawImage<Gray8> image)
    => _FromExactRawImage(image, PngColorType.Grayscale, 8);

  public static PngFile FromRawImage(RawImage<Gray16> image)
    => _FromExactRawImage(image, PngColorType.Grayscale, 16);

  public static PngFile FromRawImage(RawImage<GrayAlpha16> image)
    => _FromExactRawImage(image, PngColorType.GrayscaleAlpha, 8);

  public static PngFile FromRawImage(RawImage<GrayAlpha32> image)
    => _FromExactRawImage(image, PngColorType.GrayscaleAlpha, 16);

  public static PngFile FromRawImage(RawImage<Rgb24> image)
    => _FromExactRawImage(image, PngColorType.RGB, 8);

  public static PngFile FromRawImage(RawImage<Rgb48> image)
    => _FromExactRawImage(image, PngColorType.RGB, 16);

  public static PngFile FromRawImage(RawImage<RawRgba32> image)
    => _FromExactRawImage(image, PngColorType.RGBA, 8);

  public static PngFile FromRawImage(RawImage<Rgba64> image)
    => _FromExactRawImage(image, PngColorType.RGBA, 16);

  public static PngFile FromRawImage(RawImage<Indexed1> image)
    => _FromExactRawImage(image, PngColorType.Palette, 1);

  public static PngFile FromRawImage(RawImage<Indexed2> image)
    => _FromExactRawImage(image, PngColorType.Palette, 2);

  public static PngFile FromRawImage(RawImage<Indexed4> image)
    => _FromExactRawImage(image, PngColorType.Palette, 4);

  public static PngFile FromRawImage(RawImage<Indexed8> image)
    => _FromExactRawImage(image, PngColorType.Palette, 8);

  private static PngFile _FromExactRawImage<TPixel>(
    RawImage<TPixel> image,
    PngColorType colorType,
    int bitDepth
  ) where TPixel : IRawPixelFormat<TPixel> {
    ArgumentNullException.ThrowIfNull(image);
    image.Validate();

    if (image.Width <= 0)
      throw new ArgumentException("PNG width must be greater than zero.", nameof(image));
    if (image.Height <= 0)
      throw new ArgumentException("PNG height must be greater than zero.", nameof(image));
    if (image.PixelData.LongLength != image.MinimumPixelDataLength)
      throw new ArgumentException(
        $"PNG requires exactly {image.MinimumPixelDataLength} bytes for this raw representation; got {image.PixelData.LongLength}.",
        nameof(image));

    var isIndexed = colorType == PngColorType.Palette;
    if (isIndexed)
      _ValidateIndexedRawImage(image, bitDepth);
    else if (image.Palette is not null || image.PaletteCount != 0 || image.AlphaTable is not null)
      throw new ArgumentException("Palette data is only valid for an indexed PNG raw representation.", nameof(image));

    byte[] scanlines;
    int stride;
    if (isIndexed) {
      stride = checked((image.Width * bitDepth + 7) / 8);
      scanlines = bitDepth switch {
        1 or 4 => _AddRowPadding(image.PixelData, image.Width, image.Height, bitDepth),
        2 => _Pack2BitIndices(image.PixelData, image.Width, image.Height),
        8 => image.PixelData,
        _ => throw new ArgumentOutOfRangeException(nameof(bitDepth)),
      };
    } else {
      var bitsPerPixel = RawImage.BitsPerPixel(image.Format);
      stride = checked((image.Width * bitsPerPixel + 7) / 8);
      scanlines = image.PixelData;
    }

    var rows = _SplitIntoRows(scanlines, stride, image.Height);
    var palette = isIndexed ? image.Palette![..] : null;
    var paletteCount = isIndexed ? image.PaletteCount : 0;
    var transparency = isIndexed && image.AlphaTable is { } alphaTable ? alphaTable[..] : null;

    List<PngChunk>? chunksBeforePlte = null;
    List<PngChunk>? chunksAfterIdat = null;
    if (image.Metadata is { IsEmpty: false } metadata) {
      chunksBeforePlte = [];
      chunksAfterIdat = [];
      PngMetadataCodec.Apply(metadata, chunksBeforePlte, chunksAfterIdat);
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
      ChunksBeforePlte = chunksBeforePlte is { Count: > 0 } ? chunksBeforePlte : null,
      ChunksAfterIdat = chunksAfterIdat is { Count: > 0 } ? chunksAfterIdat : null,
    };
  }

  private static void _ValidateIndexedRawImage<TPixel>(RawImage<TPixel> image, int bitDepth)
    where TPixel : IRawPixelFormat<TPixel> {
    var maximumPaletteCount = 1 << bitDepth;
    if (image.PaletteCount is <= 0 || image.PaletteCount > maximumPaletteCount)
      throw new ArgumentException(
        $"A {bitDepth}-bit indexed PNG requires 1..{maximumPaletteCount} palette entries; got {image.PaletteCount}.",
        nameof(image));

    if (image.Palette is not { } palette || palette.Length != checked(image.PaletteCount * 3))
      throw new ArgumentException("PNG PLTE data must contain exactly three bytes per declared palette entry.", nameof(image));

    if (image.AlphaTable is { } alphaTable && alphaTable.Length > image.PaletteCount)
      throw new ArgumentException("PNG tRNS data cannot contain more entries than PLTE.", nameof(image));

    var pixelCount = checked(image.Width * image.Height);
    for (var pixel = 0; pixel < pixelCount; ++pixel)
      if (_ReadIndex(image.PixelData, image.Format, pixel) >= image.PaletteCount)
        throw new ArgumentException($"PNG pixel {pixel} references a palette entry outside PLTE.", nameof(image));
  }

  private static int _ReadIndex(byte[] data, PixelFormat storageFormat, int pixel) => storageFormat switch {
    PixelFormat.Indexed1 => data[pixel >> 3] >> (7 - (pixel & 7)) & 1,
    PixelFormat.Indexed4 => data[pixel >> 1] >> ((pixel & 1) == 0 ? 4 : 0) & 0x0F,
    PixelFormat.Indexed8 => data[pixel],
    _ => throw new ArgumentOutOfRangeException(nameof(storageFormat), storageFormat, "Not a PNG indexed raw storage format."),
  };

  private static byte[] _Pack2BitIndices(byte[] indices, int width, int height) {
    var stride = checked((width * 2 + 7) / 8);
    var result = new byte[checked(stride * height)];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var value = indices[y * width + x];
      result[y * stride + (x >> 2)] |= (byte)(value << (6 - (x & 3) * 2));
    }

    return result;
  }
}
