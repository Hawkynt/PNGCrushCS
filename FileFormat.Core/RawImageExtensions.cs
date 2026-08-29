using System;

namespace FileFormat.Core;

/// <summary>Input coercion for <c>FromRawImage</c> implementations.</summary>
public static class RawImageExtensions {

  public static RawImage EnsureFormat(this RawImage image, PixelFormat format) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format == format)
      return image;

    return PackedPixelIntrinsics.TryConvert(image, format, out var converted)
      ? converted
      : FastRawImageConverter.Convert(image, format);
  }

  public static RawImage EnsureAnyFormat(this RawImage image, params PixelFormat[] accepted) {
    ArgumentNullException.ThrowIfNull(image);
    if (accepted == null || accepted.Length == 0)
      throw new ArgumentException("At least one accepted pixel format is required.", nameof(accepted));

    foreach (var format in accepted)
      if (image.Format == format)
        return image;

    return PackedPixelIntrinsics.TryConvert(image, accepted[0], out var converted)
      ? converted
      : FastRawImageConverter.Convert(image, accepted[0]);
  }

  public static RawImage EnsureIndexedAtMost(this RawImage image, int colors) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.EnsureFormat(PixelFormat.Indexed8);
    if (indexed.PaletteCount <= colors)
      return indexed;

    var quantized = ColorQuantizer.Quantize(
      image.EnsureFormat(PixelFormat.Bgra32).PixelData, image.Width * image.Height, colors);

    var indices = new byte[image.Width * image.Height];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)quantized.Indices[i];

    return new() {
      Width = image.Width,
      Height = image.Height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = quantized.Palette,
      PaletteCount = Math.Min(colors, quantized.Palette.Length / 3),
      ColorInfo = image.ColorInfo,
      Metadata = image.Metadata,
    };
  }

  public static RawImage EnsureIndexed(this RawImage image, PixelFormat format, byte[] palette, byte[]? alphaTable = null) {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(palette);

    if (image.Format == format && (image.Palette is not { Length: > 0 } existing || _SamePalette(existing, palette)))
      return image;

    var bgra = image.EnsureFormat(PixelFormat.Bgra32);
    var result = ColorQuantizer.MapToPalette(bgra.PixelData, image.Width * image.Height, palette, alphaTable);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Format = format,
      PixelData = ColorQuantizer.PackIndices(result.Indices, format),
      Palette = result.Palette,
      PaletteCount = result.Count,
      AlphaTable = result.AlphaTable,
      ColorInfo = image.ColorInfo,
      Metadata = image.Metadata,
    };
  }

  public static RawImage EnsureIndexed(this RawImage image, PixelFormat format, FixedPalette palette) {
    ArgumentNullException.ThrowIfNull(palette);
    return image.EnsureIndexed(format, palette.ToPackedRgb());
  }

  public static RawImage EnsureIndexed(this RawImage image, PixelFormat format, int[] packedRgbPalette) {
    ArgumentNullException.ThrowIfNull(packedRgbPalette);

    var palette = new byte[packedRgbPalette.Length * 3];
    for (var i = 0; i < packedRgbPalette.Length; ++i) {
      palette[i * 3] = (byte)(packedRgbPalette[i] >> 16);
      palette[i * 3 + 1] = (byte)(packedRgbPalette[i] >> 8);
      palette[i * 3 + 2] = (byte)packedRgbPalette[i];
    }

    return image.EnsureIndexed(format, palette);
  }

  private static bool _SamePalette(byte[] left, byte[] right) {
    if (left.Length != right.Length)
      return false;

    for (var i = 0; i < left.Length; ++i)
      if (left[i] != right[i])
        return false;

    return true;
  }

  public static RawImage SampleTo(this RawImage image, int width, int height) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var source = image.EnsureFormat(PixelFormat.Rgb24);
    if (source.Width == width && source.Height == height)
      return source;

    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y) {
      var sourceY = (int)((long)y * image.Height / height);
      for (var x = 0; x < width; ++x) {
        var from = (int)(((long)sourceY * image.Width + (long)x * image.Width / width) * 3);
        var to = (y * width + x) * 3;
        rgb[to] = source.PixelData[from];
        rgb[to + 1] = source.PixelData[from + 1];
        rgb[to + 2] = source.PixelData[from + 2];
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
      ColorInfo = source.ColorInfo,
      Metadata = image.Metadata,
    };
  }

  public static int[] ToPackedArgb(this RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = image.EnsureFormat(PixelFormat.Bgra32).PixelData;
    var packed = new int[image.Width * image.Height];
    for (var i = 0; i < packed.Length; ++i) {
      var at = i * 4;
      packed[i] = (bgra[at + 3] << 24) | (bgra[at + 2] << 16) | (bgra[at + 1] << 8) | bgra[at];
    }
    return packed;
  }
}
