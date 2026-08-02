using System;
using FileFormat.Core;

namespace FileFormat.DaliST;

/// <summary>In-memory representation of an Atari ST Dali image (SD0/SD1/SD2).</summary>
public readonly record struct DaliSTFile : IImageFormatReader<DaliSTFile>, IImageToRawImage<DaliSTFile>, IImageFromRawImage<DaliSTFile>, IImageFormatWriter<DaliSTFile> {

  /// <summary>Palette size in bytes (16 words = 32 bytes).</summary>
  public const int PaletteSize = 32;

  /// <summary>Planar pixel data size.</summary>
  public const int PlanarDataSize = 32000;

  /// <summary>The exact file size: 32 + 32000 = 32032 bytes.</summary>
  /// <summary>Offset of the 32-byte palette inside the header.</summary>
  public const int PaletteOffset = 4;

  /// <summary>Dali reserves a fixed 128-byte header; the bitmap starts immediately after it.</summary>
  public const int HeaderSize = 128;

  public const int ExpectedFileSize = HeaderSize + PlanarDataSize;

  static string IImageFormatMetadata<DaliSTFile>.PrimaryExtension => ".sd0";
  static string[] IImageFormatMetadata<DaliSTFile>.FileExtensions => [".sd0", ".sd1", ".sd2"];
  static DaliSTFile IImageFormatReader<DaliSTFile>.FromSpan(ReadOnlySpan<byte> data) => DaliSTReader.FromSpan(data);

  /// <summary>
  /// Reads a named file, which is the only way the resolution can be known.
  /// </summary>
  /// <remarks>
  /// Nothing inside one of these says which of the three screens it is; only the extension does,
  /// <c>.sd0</c>, <c>.sd1</c> and <c>.sd2</c> for low, medium and high. The reader has always known
  /// that, but only the by-bytes entry was wired up here, and that one assumes low — so a high
  /// resolution picture came back 320 by 200 instead of 640 by 400, drawn from the wrong part of
  /// its own data.
  /// </remarks>
  static DaliSTFile IImageFormatReader<DaliSTFile>.FromFile(FileInfo file) => DaliSTReader.FromFile(file);
  static VideoMode[] IImageFormatMetadata<DaliSTFile>.VideoModes => [
    new("Low resolution (320x200, 16 colours)", [(320, 200)], [new IntegerRange(2, 16)]),
    new("Medium resolution (640x200, 4 colours)", [(640, 200)], [new IntegerRange(2, 4)]),
    new("High resolution (640x400, monochrome)", [(640, 400)], [2])
  ];
  static byte[] IImageFormatWriter<DaliSTFile>.ToBytes(DaliSTFile file) => DaliSTWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Resolution mode: Low (320x200, 4 planes), Medium (640x200, 2 planes), High (640x400, 1 plane).</summary>
  public DaliSTResolution Resolution { get; init; }

  /// <summary>16-entry palette of 12-bit Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; }

  /// <summary>32000 bytes of Atari ST interleaved planar pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(DaliSTFile file) {

    var numPlanes = file.Resolution switch {
      DaliSTResolution.Low => 4,
      DaliSTResolution.Medium => 2,
      DaliSTResolution.High => 1,
      _ => throw new ArgumentException($"Unsupported resolution: {file.Resolution}", nameof(file))
    };

    var chunky = PlanarConverter.AtariStToChunky(file.PixelData, file.Width, file.Height, numPlanes);
    var paletteCount = Math.Min(1 << numPlanes, file.Palette.Length);

    // One plane is the monochrome screen, which takes no colours from the file; see the helper.
    var rgb = AtariStGraphics.ScreenPalette(file.Palette.AsSpan(0, paletteCount), numPlanes);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = chunky,
      Palette = rgb,
      PaletteCount = rgb.Length / 3,
    };
  }

  public static DaliSTFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);

    var resolution = (image.Width, image.Height) switch {
      (640, 400) => DaliSTResolution.High,
      (640, 200) => DaliSTResolution.Medium,
      (320, 200) => DaliSTResolution.Low,
      _ => image.PaletteCount switch {
        <= 2 => DaliSTResolution.High,
        <= 4 => DaliSTResolution.Medium,
        _ => DaliSTResolution.Low
      }
    };

    var numPlanes = resolution switch {
      DaliSTResolution.Low => 4,
      DaliSTResolution.Medium => 2,
      DaliSTResolution.High => 1,
      _ => 4
    };

    var (width, height) = resolution switch {
      DaliSTResolution.High => (640, 400),
      DaliSTResolution.Medium => (640, 200),
      _ => (320, 200)
    };

    var planar = PlanarConverter.ChunkyToAtariSt(image.PixelData, width, height, numPlanes);
    var paletteCount = Math.Min(image.PaletteCount, 16);
    var stPalette = PlanarConverter.RgbToStPalette(image.Palette, paletteCount);
    var palette = new short[16];
    stPalette.AsSpan(0, Math.Min(stPalette.Length, 16)).CopyTo(palette);

    return new() {
      Width = width,
      Height = height,
      Resolution = resolution,
      PixelData = planar,
      Palette = palette,
    };
  }
}
