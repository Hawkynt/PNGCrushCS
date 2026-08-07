using System;
using FileFormat.Core;

namespace FileFormat.Atari8Bit;

/// <summary>In-memory representation of an Atari 8-bit ANTIC mode screen dump.</summary>
public readonly record struct Atari8BitFile : IImageFormatReader<Atari8BitFile>, IImageToRawImage<Atari8BitFile>, IImageFromRawImage<Atari8BitFile>, IImageFormatWriter<Atari8BitFile> {

  static string IImageFormatMetadata<Atari8BitFile>.PrimaryExtension => ".gr8";
  static string[] IImageFormatMetadata<Atari8BitFile>.FileExtensions => [".gr7", ".gr8", ".gr9", ".gr15", ".hip", ".mic", ".int"];
  static Atari8BitFile IImageFormatReader<Atari8BitFile>.FromSpan(ReadOnlySpan<byte> data) => Atari8BitReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Atari8BitFile>.VideoModes => [
    new("Default", [(320, 192), (160, 192), (80, 192), (160, 96)], [new IntegerRange(2, 16)])
  ];
  static byte[] IImageFormatWriter<Atari8BitFile>.ToBytes(Atari8BitFile file) => Atari8BitWriter.ToBytes(file);

  /// <summary>Width in pixels (depends on mode: 320, 160, or 80).</summary>
  public int Width { get; init; }

  /// <summary>Height in pixels (depends on mode: 192 or 96).</summary>
  public int Height { get; init; }

  /// <summary>Graphics mode.</summary>
  public Atari8BitMode Mode { get; init; }

  /// <summary>Indexed pixel data (one byte per pixel, values are palette indices).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>RGB palette triplets (3 bytes per entry).</summary>
  public byte[] Palette { get; init; }

  /// <summary>File size for GR.8/GR.9/GR.15: 40 bytes/row x 192 rows.</summary>
  internal const int FileSize7680 = 7680;

  /// <summary>File size for GR.7: 20 bytes/row x 96 rows.</summary>
  internal const int FileSize1920 = 1920;

  /// <summary>Gets the expected file size for a given mode.</summary>
  internal static int GetFileSize(Atari8BitMode mode) => mode switch {
    Atari8BitMode.Gr7 => FileSize1920,
    Atari8BitMode.Gr8 => FileSize7680,
    Atari8BitMode.Gr9 => FileSize7680,
    Atari8BitMode.Gr15 => FileSize7680,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Atari 8-bit mode.")
  };

  /// <summary>Gets the pixel width for a given mode.</summary>
  internal static int GetWidth(Atari8BitMode mode) => mode switch {
    Atari8BitMode.Gr7 => 160,
    Atari8BitMode.Gr8 => 320,
    Atari8BitMode.Gr9 => 80,
    Atari8BitMode.Gr15 => 160,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Atari 8-bit mode.")
  };

  /// <summary>Gets the pixel height for a given mode.</summary>
  internal static int GetHeight(Atari8BitMode mode) => mode switch {
    Atari8BitMode.Gr7 => 96,
    Atari8BitMode.Gr8 => 192,
    Atari8BitMode.Gr9 => 192,
    Atari8BitMode.Gr15 => 192,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Atari 8-bit mode.")
  };

  /// <summary>Gets the bits per pixel for a given mode.</summary>
  internal static int GetBitsPerPixel(Atari8BitMode mode) => mode switch {
    Atari8BitMode.Gr7 => 2,
    Atari8BitMode.Gr8 => 1,
    Atari8BitMode.Gr9 => 4,
    Atari8BitMode.Gr15 => 2,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Atari 8-bit mode.")
  };

  /// <summary>Gets the raw bytes per row stored in the file for a given mode.</summary>
  internal static int GetBytesPerRow(Atari8BitMode mode) => mode switch {
    Atari8BitMode.Gr7 => 20,
    Atari8BitMode.Gr8 => 40,
    Atari8BitMode.Gr9 => 40,
    Atari8BitMode.Gr15 => 40,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Atari 8-bit mode.")
  };

  /// <summary>Gets the horizontal pixel scale factor for a given mode (pixel doubling).</summary>
  internal static int GetPixelScale(Atari8BitMode mode) => mode switch {
    Atari8BitMode.Gr7 => 2,
    Atari8BitMode.Gr8 => 1,
    Atari8BitMode.Gr9 => 1,
    Atari8BitMode.Gr15 => 1,
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Atari 8-bit mode.")
  };

  /// <summary>Gets the default palette for a given mode as RGB triplets.</summary>
  internal static byte[] GetDefaultPalette(Atari8BitMode mode) => mode switch {
    Atari8BitMode.Gr8 => Atari8BitGraphics.MonochromePalette.ToArray(),
    Atari8BitMode.Gr9 => _BuildGrayscale16Palette(),
    Atari8BitMode.Gr15 => [0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255],
    Atari8BitMode.Gr7 => [0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255],
    _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown Atari 8-bit mode.")
  };

  private static byte[] _BuildGrayscale16Palette() {
    var palette = new byte[16 * 3];
    for (var i = 0; i < 16; ++i) {
      var v = (byte)(i * 17);
      palette[i * 3] = v;
      palette[i * 3 + 1] = v;
      palette[i * 3 + 2] = v;
    }
    return palette;
  }

  /// <summary>Converts this Atari 8-bit screen to a platform-independent <see cref="RawImage"/> in Indexed8 format.</summary>
  public static RawImage ToRawImage(Atari8BitFile file) {

    var palette = file.Palette.Length > 0 ? file.Palette[..] : GetDefaultPalette(file.Mode);
    var paletteCount = palette.Length / 3;

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates an Atari 8-bit screen from a <see cref="RawImage"/>. Accepts Indexed1 or Indexed8.</summary>
  public static Atari8BitFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Indexed1);
    if (image.Format != PixelFormat.Indexed8 && image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} or {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));

    var mode = _InferModeFromDimensions(image.Width, image.Height, image.PaletteCount);
    var pixels = image.Format == PixelFormat.Indexed1
      ? _ExpandIndexed1ToBytes(image.PixelData, image.Width, image.Height)
      : image.PixelData[..];

    return new() {
      Width = image.Width,
      Height = image.Height,
      Mode = mode,
      PixelData = pixels,
      Palette = image.Palette != null ? image.Palette[..] : GetDefaultPalette(mode),
    };
  }

  private static byte[] _ExpandIndexed1ToBytes(byte[] packed, int width, int height) {
    var stride = (width + 7) / 8;
    var result = new byte[width * height];
    for (var y = 0; y < height; ++y) {
      var rowSrc = y * stride;
      var rowDst = y * width;
      for (var x = 0; x < width; ++x) {
        var b = packed[rowSrc + (x >> 3)];
        result[rowDst + x] = (byte)((b >> (7 - (x & 7))) & 1);
      }
    }
    return result;
  }

  private static Atari8BitMode _InferModeFromDimensions(int width, int height, int paletteCount) {
    if (width == 160 && height == 96)
      return Atari8BitMode.Gr7;
    if (width == 320 && height == 192)
      return Atari8BitMode.Gr8;
    if (width == 80 && height == 192)
      return Atari8BitMode.Gr9;
    if (width == 160 && height == 192)
      return Atari8BitMode.Gr15;

    throw new ArgumentException($"Cannot infer Atari 8-bit mode from dimensions {width}x{height}.", nameof(width));
  }
}
