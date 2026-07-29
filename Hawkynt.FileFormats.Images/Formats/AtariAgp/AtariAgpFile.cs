using System;
using FileFormat.Core;

namespace FileFormat.AtariAgp;

/// <summary>In-memory representation of an Atari 8-bit AGP (Atari Graphics Processor) image.</summary>
public readonly record struct AtariAgpFile : IImageFormatReader<AtariAgpFile>, IImageToRawImage<AtariAgpFile>, IImageFromRawImage<AtariAgpFile>, IImageFormatWriter<AtariAgpFile> {

  /// <summary>File size for Graphics 8 mode (320x192, 1bpp).</summary>
  internal const int FileSizeGr8 = 7680;

  /// <summary>File size for Graphics 7 mode (160x96, 2bpp).</summary>
  internal const int FileSizeGr7 = 3840;

  /// <summary>File size for Graphics 8 with 2 appended color bytes.</summary>
  internal const int FileSizeGr8WithColors = 7682;

  static string IImageFormatMetadata<AtariAgpFile>.PrimaryExtension => ".agp";
  static string[] IImageFormatMetadata<AtariAgpFile>.FileExtensions => [".agp"];
  static AtariAgpFile IImageFormatReader<AtariAgpFile>.FromSpan(ReadOnlySpan<byte> data) => AtariAgpReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariAgpFile>.VideoModes => [
    new("Default", [(320, 192), (160, 96)], [new IntegerRange(2, 4)])
  ];
  static byte[] IImageFormatWriter<AtariAgpFile>.ToBytes(AtariAgpFile file) => AtariAgpWriter.ToBytes(file);

  /// <summary>Width in pixels (320 for GR.8, 160 for GR.7).</summary>
  public int Width { get; init; }

  /// <summary>Height in pixels (192 for GR.8, 96 for GR.7).</summary>
  public int Height { get; init; }

  /// <summary>Detected graphics mode.</summary>
  public AtariAgpMode Mode { get; init; }

  /// <summary>Indexed pixel data (one byte per pixel).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>RGB palette triplets (3 bytes per entry).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Optional foreground color byte (from GR.8 with colors variant).</summary>
  public byte ForegroundColor { get; init; }

  /// <summary>Optional background color byte (from GR.8 with colors variant).</summary>
  public byte BackgroundColor { get; init; }

  /// <summary>Default B&amp;W palette for GR.8 modes.</summary>
  internal static readonly byte[] DefaultGr8Palette = [0, 0, 0, 255, 255, 255];

  /// <summary>Default 4-color grayscale palette for GR.7 mode.</summary>
  internal static readonly byte[] DefaultGr7Palette = [0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255];

  /// <summary>Gets the width for a given mode.</summary>
  internal static int GetWidth(AtariAgpMode mode) => mode switch {
    AtariAgpMode.Graphics7 => 160,
    _ => 320,
  };

  /// <summary>Gets the height for a given mode.</summary>
  internal static int GetHeight(AtariAgpMode mode) => mode switch {
    AtariAgpMode.Graphics7 => 96,
    _ => 192,
  };

  /// <summary>Gets the default palette for a given mode.</summary>
  internal static byte[] GetDefaultPalette(AtariAgpMode mode) => mode switch {
    AtariAgpMode.Graphics7 => DefaultGr7Palette[..],
    _ => DefaultGr8Palette[..],
  };

  /// <summary>Converts this AGP image to an Indexed8 raw image.</summary>
  public static RawImage ToRawImage(AtariAgpFile file) {

    var width = file.Width;
    var height = file.Height;
    var palette = file.Palette.Length > 0 ? file.Palette[..] : GetDefaultPalette(file.Mode);
    var paletteCount = palette.Length / 3;

    var pixelData = new byte[width * height];
    var srcLen = Math.Min(file.PixelData.Length, pixelData.Length);
    file.PixelData.AsSpan(0, srcLen).CopyTo(pixelData);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixelData,
      Palette = palette,
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates an AGP image from an Indexed1 or Indexed8 raw image.</summary>
  public static AtariAgpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var mode = _InferMode(image.Width, image.Height);

    // Graphics 7 is a 4-colour mode, so it must not be coerced down to the 2-colour Indexed1
    // layout that Graphics 8 uses.
    image = image.EnsureAnyFormat(mode == AtariAgpMode.Graphics7 ? PixelFormat.Indexed8 : PixelFormat.Indexed1);
    var pixelData = new byte[image.Width * image.Height];
    if (image.Format == PixelFormat.Indexed1) {
      var stride = (image.Width + 7) / 8;
      for (var y = 0; y < image.Height; ++y) {
        var rowSrc = y * stride;
        var rowDst = y * image.Width;
        for (var x = 0; x < image.Width; ++x) {
          var b = image.PixelData[rowSrc + (x >> 3)];
          pixelData[rowDst + x] = (byte)((b >> (7 - (x & 7))) & 1);
        }
      }
    } else {
      var srcLen = Math.Min(image.PixelData.Length, pixelData.Length);
      image.PixelData.AsSpan(0, srcLen).CopyTo(pixelData);
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      Mode = mode,
      PixelData = pixelData,
      Palette = image.Palette != null ? image.Palette[..] : GetDefaultPalette(mode),
    };
  }

  private static AtariAgpMode _InferMode(int width, int height) {
    if (width == 160 && height == 96)
      return AtariAgpMode.Graphics7;
    if (width == 320 && height == 192)
      return AtariAgpMode.Graphics8;

    throw new ArgumentException($"Cannot infer AGP mode from dimensions {width}x{height}.", nameof(width));
  }
}
