using System;
using FileFormat.Core;

namespace FileFormat.AtariPaintworks;

/// <summary>In-memory representation of an Atari ST Paintworks/GFA/DeskPic image file.</summary>
public readonly record struct AtariPaintworksFile : IImageFormatReader<AtariPaintworksFile>, IImageToRawImage<AtariPaintworksFile>, IImageFromRawImage<AtariPaintworksFile>, IImageFormatWriter<AtariPaintworksFile> {

  /// <summary>Offset of the 32-byte ST palette.</summary>
  public const int PaletteOffset = 4;

  /// <summary>Offset of the ASCII signature that identifies the format.</summary>
  public const int SignatureOffset = 54;

  /// <summary>ASCII signature every Paintworks screen carries.</summary>
  public static System.ReadOnlySpan<byte> Signature => "ANvisionA"u8;

  /// <summary>Offset of the flags byte: low nibble selects the line count, bits 4-5 the ST
  /// resolution, and bit 7 marks the bitmap as RLE-compressed.</summary>
  public const int FlagsOffset = 63;

  /// <summary>Flags for an uncompressed 320x200 low-resolution screen.</summary>
  public const byte LowResolutionFlags = 0x01;

  /// <summary>The bit the flags byte sets for the monochrome screen.</summary>
  public const byte HighResolutionFlag = 0x20;

  /// <summary>Offset of the bitmap.</summary>
  public const int BitmapOffset = 128;

  /// <summary>Bitmap size for a single-height screen.</summary>
  public const int BitmapDataSize = 32000;

  /// <summary>Total size of an uncompressed single-height file.</summary>
  public const int FileSize = BitmapOffset + BitmapDataSize;


  static string IImageFormatMetadata<AtariPaintworksFile>.PrimaryExtension => ".cl0";
  static string[] IImageFormatMetadata<AtariPaintworksFile>.FileExtensions => [".cl0", ".cl1", ".cl2", ".pg0", ".pg1", ".pg2", ".pg3", ".sc0", ".sc1", ".sc2"];
  static AtariPaintworksFile IImageFormatReader<AtariPaintworksFile>.FromSpan(ReadOnlySpan<byte> data) => AtariPaintworksReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariPaintworksFile>.VideoModes => [
    new("Low resolution (320x200, 16 colours)", [(320, 200)], [new IntegerRange(2, 16)]),
    new("Medium resolution (640x200, 4 colours)", [(640, 200)], [new IntegerRange(2, 4)]),
    new("High resolution (640x400, monochrome)", [(640, 400)], [2])
  ];
  static byte[] IImageFormatWriter<AtariPaintworksFile>.ToBytes(AtariPaintworksFile file) => AtariPaintworksWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Resolution mode determining dimensions and color depth.</summary>
  public AtariPaintworksResolution Resolution { get; init; }

  /// <summary>16-entry palette of 9-bit Atari ST RGB values (0x0RGB, R/G/B in 0-7).</summary>
  public short[] Palette { get; init; }

  /// <summary>Atari ST word-interleaved planar pixel data (32000 bytes for full screen).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(AtariPaintworksFile file) {

    var numPlanes = file.Resolution switch {
      AtariPaintworksResolution.Low => 4,
      AtariPaintworksResolution.Medium => 2,
      AtariPaintworksResolution.High => 1,
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

  public static AtariPaintworksFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed8);

    var resolution = (image.Width, image.Height) switch {
      (640, 400) => AtariPaintworksResolution.High,
      (640, 200) => AtariPaintworksResolution.Medium,
      (320, 200) => AtariPaintworksResolution.Low,
      _ => image.PaletteCount switch {
        <= 2 => AtariPaintworksResolution.High,
        <= 4 => AtariPaintworksResolution.Medium,
        _ => AtariPaintworksResolution.Low
      }
    };

    var numPlanes = resolution switch {
      AtariPaintworksResolution.Low => 4,
      AtariPaintworksResolution.Medium => 2,
      AtariPaintworksResolution.High => 1,
      _ => 4
    };

    var (width, height) = resolution switch {
      AtariPaintworksResolution.High => (640, 400),
      AtariPaintworksResolution.Medium => (640, 200),
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
