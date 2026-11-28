using System;
using FileFormat.Core;

namespace FileFormat.LuraDocument;

/// <summary>What a LuraDocument page decodes to, which follows from the layers it carries.</summary>
public enum LuraDocumentPageKind {

  /// <summary>A mask alone: black text on white, one byte a pixel holding 0 (black) or 1 (white).</summary>
  Bilevel,

  /// <summary>Grey layers: one byte a pixel.</summary>
  Grey,

  /// <summary>At least one colour layer: red, green and blue bytes a pixel.</summary>
  Colour,
}

/// <summary>One page of a LuraDocument file: its decoded picture and the layers it was built from.</summary>
public sealed record LuraDocumentPage {

  /// <summary>Page width in pixels, the size of its mask.</summary>
  public int Width { get; init; }

  /// <summary>Page height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>How <see cref="PixelData"/> is laid out.</summary>
  public LuraDocumentPageKind Kind { get; init; }

  /// <summary>The composed page: 0/1 per pixel for <see cref="LuraDocumentPageKind.Bilevel"/>, grey or RGB bytes otherwise.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Horizontal resolution in dots per inch.</summary>
  public int HorizontalDpi { get; init; } = LuraDocumentFile.DefaultDpi;

  /// <summary>Vertical resolution in dots per inch.</summary>
  public int VerticalDpi { get; init; } = LuraDocumentFile.DefaultDpi;

  /// <summary>Whether the page has a bilevel mask choosing between foreground and background.</summary>
  public bool HasMask { get; init; }

  /// <summary>Size of the foreground layer, or <see langword="null"/> when the page has none.</summary>
  public (int Width, int Height)? ForegroundSize { get; init; }

  /// <summary>Size of the background layer, or <see langword="null"/> when the page has none.</summary>
  public (int Width, int Height)? BackgroundSize { get; init; }

  /// <summary>The bytes per pixel of <see cref="PixelData"/>.</summary>
  public int BytesPerPixel => this.Kind == LuraDocumentPageKind.Colour ? 3 : 1;

  private static readonly byte[] _BlackWhite = [0, 0, 0, 255, 255, 255];

  /// <summary>The page as a raw image: black-and-white indexed, Gray8 or Rgb24.</summary>
  public RawImage ToRawImage() => this.Kind switch {
    LuraDocumentPageKind.Bilevel => new() {
      Width = this.Width,
      Height = this.Height,
      Format = PixelFormat.Indexed8,
      PixelData = this.PixelData[..],
      Palette = _BlackWhite[..],
      PaletteCount = 2,
    },
    LuraDocumentPageKind.Grey => new() { Width = this.Width, Height = this.Height, Format = PixelFormat.Gray8, PixelData = this.PixelData[..] },
    _ => new() { Width = this.Width, Height = this.Height, Format = PixelFormat.Rgb24, PixelData = this.PixelData[..] },
  };

  /// <summary>
  /// Builds a page from pixels: a two-colour black-and-white picture becomes a bilevel page, grey a grey page and
  /// everything else a colour page (alpha is dropped: the format has none).
  /// </summary>
  public static LuraDocumentPage FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (_IsBlackAndWhite(image, out var bits))
      return new() { Width = image.Width, Height = image.Height, Kind = LuraDocumentPageKind.Bilevel, PixelData = bits };

    var rgb = image.EnsureFormat(PixelFormat.Rgb24).PixelData;
    var pixels = image.Width * image.Height;
    var grey = true;
    for (var i = 0; grey && i < pixels; ++i)
      grey = rgb[i * 3] == rgb[i * 3 + 1] && rgb[i * 3] == rgb[i * 3 + 2];
    if (!grey)
      return new() { Width = image.Width, Height = image.Height, Kind = LuraDocumentPageKind.Colour, PixelData = rgb[..(pixels * 3)] };

    var levels = new byte[pixels];
    for (var i = 0; i < pixels; ++i)
      levels[i] = rgb[i * 3];
    return new() { Width = image.Width, Height = image.Height, Kind = LuraDocumentPageKind.Grey, PixelData = levels };
  }

  private static bool _IsBlackAndWhite(RawImage image, out byte[] bits) {
    bits = [];
    if (image.Format is not (PixelFormat.Indexed1 or PixelFormat.Indexed4 or PixelFormat.Indexed8) || image.Palette is null)
      return false;

    var count = image.PaletteCount > 0 ? image.PaletteCount : image.Palette.Length / 3;
    if (count is < 1 or > 2)
      return false;
    for (var i = 0; i < count * 3; ++i)
      if (image.Palette[i] is not (0 or 255) || image.Palette[i] != image.Palette[i / 3 * 3])
        return false;

    var rgb = image.EnsureFormat(PixelFormat.Rgb24).PixelData;
    bits = new byte[image.Width * image.Height];
    for (var i = 0; i < bits.Length; ++i)
      bits[i] = (byte)(rgb[i * 3] == 255 ? 1 : 0);
    return true;
  }
}
