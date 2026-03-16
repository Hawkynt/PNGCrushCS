using System;

namespace FileFormat.Core;

/// <summary>Platform-independent pixel buffer that serves as the intermediate type for cross-format image conversion.</summary>
public sealed class RawImage {

  /// <summary>The width of the image in pixels.</summary>
  public required int Width { get; init; }

  /// <summary>The height of the image in pixels.</summary>
  public required int Height { get; init; }

  /// <summary>The pixel format describing how bytes in <see cref="PixelData"/> are laid out.</summary>
  public required PixelFormat Format { get; init; }

  /// <summary>The raw pixel data in the layout described by <see cref="Format"/>.</summary>
  public required byte[] PixelData { get; init; }

  /// <summary>Optional palette entries as RGB triplets (3 bytes per entry). Required for indexed pixel formats.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>The number of entries in the palette (0 when no palette is present).</summary>
  public int PaletteCount { get; init; }

  /// <summary>Optional per-palette-entry alpha values. Used for PNG tRNS-style transparency on indexed images.</summary>
  public byte[]? AlphaTable { get; init; }

  /// <summary>Whether this image uses an indexed pixel format.</summary>
  public bool IsIndexed => Format is PixelFormat.Indexed8 or PixelFormat.Indexed4 or PixelFormat.Indexed1;

  /// <summary>Whether this image has an alpha channel (format-based check with alpha table scan for indexed formats).</summary>
  public bool HasAlpha {
    get {
      switch (Format) {
        case PixelFormat.Bgra32:
        case PixelFormat.Rgba32:
        case PixelFormat.Argb32:
        case PixelFormat.Rgba64:
        case PixelFormat.GrayAlpha16:
          return true;
        case PixelFormat.Indexed8:
        case PixelFormat.Indexed4:
        case PixelFormat.Indexed1:
          if (AlphaTable == null)
            return false;
          foreach (var a in AlphaTable)
            if (a < 255)
              return true;
          return false;
        default:
          return false;
      }
    }
  }

  /// <summary>Converts this image to BGRA32 pixel data.</summary>
  public byte[] ToBgra32() => Format == PixelFormat.Bgra32 ? PixelData : PixelConverter.Convert(this, PixelFormat.Bgra32).PixelData;

  /// <summary>Converts this image to RGBA32 pixel data.</summary>
  public byte[] ToRgba32() => Format == PixelFormat.Rgba32 ? PixelData : PixelConverter.Convert(this, PixelFormat.Rgba32).PixelData;

  /// <summary>Converts this image to RGB24 pixel data.</summary>
  public byte[] ToRgb24() => Format == PixelFormat.Rgb24 ? PixelData : PixelConverter.Convert(this, PixelFormat.Rgb24).PixelData;

  /// <summary>Computes the number of bytes per pixel for the given format, or 0 for sub-byte formats.</summary>
  public static int BytesPerPixel(PixelFormat format) => format switch {
    PixelFormat.Bgra32 => 4,
    PixelFormat.Rgba32 => 4,
    PixelFormat.Argb32 => 4,
    PixelFormat.Rgb24 => 3,
    PixelFormat.Bgr24 => 3,
    PixelFormat.Gray8 => 1,
    PixelFormat.Gray16 => 2,
    PixelFormat.GrayAlpha16 => 2,
    PixelFormat.Indexed8 => 1,
    PixelFormat.Indexed4 => 0,
    PixelFormat.Indexed1 => 0,
    PixelFormat.Rgba64 => 8,
    PixelFormat.Rgb48 => 6,
    PixelFormat.Rgb565 => 2,
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
  };

  /// <summary>Computes the number of bits per pixel for the given format.</summary>
  public static int BitsPerPixel(PixelFormat format) => format switch {
    PixelFormat.Bgra32 => 32,
    PixelFormat.Rgba32 => 32,
    PixelFormat.Argb32 => 32,
    PixelFormat.Rgb24 => 24,
    PixelFormat.Bgr24 => 24,
    PixelFormat.Gray8 => 8,
    PixelFormat.Gray16 => 16,
    PixelFormat.GrayAlpha16 => 16,
    PixelFormat.Indexed8 => 8,
    PixelFormat.Indexed4 => 4,
    PixelFormat.Indexed1 => 1,
    PixelFormat.Rgba64 => 64,
    PixelFormat.Rgb48 => 48,
    PixelFormat.Rgb565 => 16,
    _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
  };
}
