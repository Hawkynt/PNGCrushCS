using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Atari8Bit;

/// <summary>In-memory representation of an Atari 8-bit ANTIC mode screen dump.</summary>
public sealed class Atari8BitFile : IImageFileFormat<Atari8BitFile> {

  static string IImageFileFormat<Atari8BitFile>.PrimaryExtension => ".gr8";
  static string[] IImageFileFormat<Atari8BitFile>.FileExtensions => [".gr7", ".gr8", ".gr9", ".gr15", ".hip", ".mic", ".int"];
  static FormatCapability IImageFileFormat<Atari8BitFile>.Capabilities => FormatCapability.IndexedOnly;
  static Atari8BitFile IImageFileFormat<Atari8BitFile>.FromFile(FileInfo file) => Atari8BitReader.FromFile(file);
  static Atari8BitFile IImageFileFormat<Atari8BitFile>.FromBytes(byte[] data) => Atari8BitReader.FromBytes(data);
  static Atari8BitFile IImageFileFormat<Atari8BitFile>.FromStream(Stream stream) => Atari8BitReader.FromStream(stream);
  static byte[] IImageFileFormat<Atari8BitFile>.ToBytes(Atari8BitFile file) => Atari8BitWriter.ToBytes(file);

  /// <summary>Width in pixels (depends on mode: 320, 160, or 80).</summary>
  public int Width { get; init; }

  /// <summary>Height in pixels (depends on mode: 192 or 96).</summary>
  public int Height { get; init; }

  /// <summary>Graphics mode.</summary>
  public Atari8BitMode Mode { get; init; }

  /// <summary>Indexed pixel data (one byte per pixel, values are palette indices).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>RGB palette triplets (3 bytes per entry).</summary>
  public byte[] Palette { get; init; } = [];

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
    Atari8BitMode.Gr8 => [0, 0, 0, 255, 255, 255],
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
    ArgumentNullException.ThrowIfNull(file);

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

  /// <summary>Creates an Atari 8-bit screen from a <see cref="RawImage"/>. Expects Indexed8 format.</summary>
  public static Atari8BitFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Expected {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));

    var mode = _InferModeFromDimensions(image.Width, image.Height, image.PaletteCount);

    return new() {
      Width = image.Width,
      Height = image.Height,
      Mode = mode,
      PixelData = image.PixelData[..],
      Palette = image.Palette != null ? image.Palette[..] : GetDefaultPalette(mode),
    };
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
