using System;

namespace FileFormat.Core;

/// <summary>Input coercion for <c>FromRawImage</c> implementations.</summary>
/// <remarks>
/// <see cref="FormatIO.Encode{TFormat}"/> accepts any <see cref="RawImage"/>, so a writer that only
/// understands one pixel layout has to convert rather than reject — otherwise encoding succeeds only
/// for callers who already guessed the format's internal layout. The conversion entry point here is
/// <see cref="FastRawImageConverter"/>, which adds fast RGB↔planar-YUV routes, delegates floating-point
/// handling to <see cref="RawImageConverter"/>, and keeps the packed integer SIMD paths in
/// <see cref="PixelConverter"/>.
/// </remarks>
public static class RawImageExtensions {

  /// <summary>Returns the image in <paramref name="format"/>, converting only when it isn't already.</summary>
  public static RawImage EnsureFormat(this RawImage image, PixelFormat format) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format == format)
      return image;

    return PackedPixelIntrinsics.TryConvert(image, format, out var converted)
      ? converted
      : FastRawImageConverter.Convert(image, format);
  }

  /// <summary>Returns the image unchanged when it already uses one of <paramref name="accepted"/>,
  /// otherwise converts it to the first entry — so list the format's preferred layout first.</summary>
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

  /// <summary>Returns the image as <paramref name="format"/> with its indices addressing
  /// <paramref name="palette"/>. Use this for formats whose palette is fixed by the hardware or the
  /// spec: a generic quantizer would build its own palette and the indices would decode to the wrong
  /// colours.</summary>
  /// <param name="image">Source image.</param>
  /// <param name="format">Target indexed pixel format.</param>
  /// <param name="palette">The format's fixed palette as RGB triplets.</param>
  /// <param name="alphaTable">Optional per-entry alpha; entries default to opaque when omitted.</param>
  /// <summary>Reduces a picture to at most a given number of colours, choosing them from it.</summary>
  /// <remarks>
  /// Converting to an indexed format alone gives whatever palette the picture needs, which is often
  /// 256 — and a format holding two or four then has to refuse it. Refusing is the wrong answer for
  /// something whose job is converting between formats: the caller asked for this format, and the
  /// format's limit is a fact about it rather than a fault in the picture.
  /// </remarks>
  public static RawImage EnsureIndexedAtMost(this RawImage image, int colors) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.EnsureFormat(PixelFormat.Indexed8);
    if (indexed.PaletteCount <= colors)
      return indexed;

    var quantized = ColorQuantizer.Quantize(
      FastRawImageConverter.Convert(image, PixelFormat.Bgra32).PixelData, image.Width * image.Height, colors);

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

    // Already in the target layout: either the indices address this very palette, or the image
    // carries no palette to interpret them against. Round-tripping through RGB would only lose
    // information — palettes with duplicate colours collapse distinct indices onto one.
    if (image.Format == format && (image.Palette is not { Length: > 0 } existing || _SamePalette(existing, palette)))
      return image;

    var bgra = image.Format == PixelFormat.Bgra32 ? image : FastRawImageConverter.Convert(image, PixelFormat.Bgra32);
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

  /// <summary>Returns the image as <paramref name="format"/>, mapped onto the given
  /// <see cref="FixedPalette"/>.</summary>
  public static RawImage EnsureIndexed(this RawImage image, PixelFormat format, FixedPalette palette) {
    ArgumentNullException.ThrowIfNull(palette);
    return image.EnsureIndexed(format, palette.ToPackedRgb());
  }

  /// <summary>Returns the image as <paramref name="format"/>, mapped onto a palette given as packed
  /// <c>0xRRGGBB</c> values — the shape most vintage formats declare their hardware palette in.</summary>
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

  /// <summary>Samples a picture to a fixed size, as three bytes a pixel.</summary>
  /// <remarks>
  /// Most of the machines here have one screen size and no other, so a picture of any other size has
  /// to be brought to theirs before anything else can happen. Nearest neighbour, deliberately: what
  /// follows is a reduction to a handful of colours, and smoothing the source first only invents
  /// shades that then have to be quantised away again.
  /// </remarks>
  public static RawImage SampleTo(this RawImage image, int width, int height) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var source = FastRawImageConverter.Convert(image, PixelFormat.Rgb24);
    if (source.Width == width && source.Height == height)
      return source;

    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y) {
      // In long arithmetic. A source wider than about 32768 overflows a signed int part way along a
      // row, and the offset comes out negative — so the widest pictures the headers here can state
      // were the ones that threw.
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

  /// <summary>The picture as packed 0xAARRGGBB values, one per pixel, row by row.</summary>
  /// <remarks>
  /// This is the shape a UI toolkit wants when it hands pixels to the platform: one integer a pixel
  /// in the machine's own order, rather than a byte array whose channel order has to be agreed. It
  /// is deliberately not a platform type — the caller decides what to build from it, so a picture
  /// can reach a screen without this project knowing which toolkit is drawing it.
  /// </remarks>
  public static int[] ToPackedArgb(this RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = FastRawImageConverter.Convert(image, PixelFormat.Bgra32).PixelData;
    var packed = new int[image.Width * image.Height];

    for (var i = 0; i < packed.Length; ++i) {
      var at = i * 4;
      packed[i] = (bgra[at + 3] << 24) | (bgra[at + 2] << 16) | (bgra[at + 1] << 8) | bgra[at];
    }

    return packed;
  }
}
