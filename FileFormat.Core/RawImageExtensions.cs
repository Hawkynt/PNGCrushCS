using System;

namespace FileFormat.Core;

/// <summary>Input coercion for <c>FromRawImage</c> implementations.</summary>
/// <remarks>
/// <see cref="FormatIO.Encode{TFormat}"/> accepts any <see cref="RawImage"/>, so a writer that only
/// understands one pixel layout has to convert rather than reject — otherwise encoding succeeds only
/// for callers who already guessed the format's internal layout.
/// </remarks>
public static class RawImageExtensions {

  /// <summary>Returns the image in <paramref name="format"/>, converting only when it isn't already.</summary>
  public static RawImage EnsureFormat(this RawImage image, PixelFormat format) {
    ArgumentNullException.ThrowIfNull(image);
    return image.Format == format ? image : PixelConverter.Convert(image, format);
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

    return PixelConverter.Convert(image, accepted[0]);
  }

  /// <summary>Returns the image as <paramref name="format"/> with its indices addressing
  /// <paramref name="palette"/>. Use this for formats whose palette is fixed by the hardware or the
  /// spec: a generic quantizer would build its own palette and the indices would decode to the wrong
  /// colours.</summary>
  /// <param name="image">Source image.</param>
  /// <param name="format">Target indexed pixel format.</param>
  /// <param name="palette">The format's fixed palette as RGB triplets.</param>
  /// <param name="alphaTable">Optional per-entry alpha; entries default to opaque when omitted.</param>
  public static RawImage EnsureIndexed(this RawImage image, PixelFormat format, byte[] palette, byte[]? alphaTable = null) {
    ArgumentNullException.ThrowIfNull(image);
    ArgumentNullException.ThrowIfNull(palette);

    // Already in the target layout: either the indices address this very palette, or the image
    // carries no palette to interpret them against. Round-tripping through RGB would only lose
    // information — palettes with duplicate colours collapse distinct indices onto one.
    if (image.Format == format && (image.Palette is not { Length: > 0 } existing || _SamePalette(existing, palette)))
      return image;

    var bgra = image.Format == PixelFormat.Bgra32 ? image : PixelConverter.Convert(image, PixelFormat.Bgra32);
    var result = ColorQuantizer.MapToPalette(bgra.PixelData, image.Width * image.Height, palette, alphaTable);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Format = format,
      PixelData = ColorQuantizer.PackIndices(result.Indices, format),
      Palette = result.Palette,
      PaletteCount = result.Count,
      AlphaTable = result.AlphaTable,
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
}
