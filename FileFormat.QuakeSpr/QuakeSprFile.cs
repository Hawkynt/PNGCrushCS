using System;
using FileFormat.Core;

namespace FileFormat.QuakeSpr;

/// <summary>In-memory representation of a Quake 1 sprite (.spr) file.</summary>
[FormatMagicBytes([0x49, 0x44, 0x53, 0x50])]
public readonly record struct QuakeSprFile : IImageFormatReader<QuakeSprFile>, IImageToRawImage<QuakeSprFile>, IImageFromRawImage<QuakeSprFile>, IImageFormatWriter<QuakeSprFile> {

  static string IImageFormatMetadata<QuakeSprFile>.PrimaryExtension => ".spr";
  static string[] IImageFormatMetadata<QuakeSprFile>.FileExtensions => [".spr"];
  static QuakeSprFile IImageFormatReader<QuakeSprFile>.FromSpan(ReadOnlySpan<byte> data) => QuakeSprReader.FromSpan(data);
  static byte[] IImageFormatWriter<QuakeSprFile>.ToBytes(QuakeSprFile file) => QuakeSprWriter.ToBytes(file);

  /// <summary>Width of the first frame in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Height of the first frame in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Sprite orientation type (0=VP_PARALLEL_UPRIGHT, 1=FACING_UPRIGHT, 2=VP_PARALLEL, 3=ORIENTED, 4=VP_PARALLEL_ORIENTED).</summary>
  public int SpriteType { get; init; }

  /// <summary>Number of frames in the sprite.</summary>
  public int NumFrames { get; init; }

  /// <summary>Bounding radius for the sprite.</summary>
  public float BoundingRadius { get; init; }

  /// <summary>Beam length for the sprite.</summary>
  public float BeamLength { get; init; }

  /// <summary>Sync type (0=synchronized, 1=random).</summary>
  public int SyncType { get; init; }

  /// <summary>Indexed8 pixel data (one byte per pixel, palette indices).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>768-byte palette (256 entries x 3 bytes RGB).</summary>
  public byte[] Palette { get; init; }

  /// <summary>Default grayscale ramp palette (256 entries).</summary>
  internal static readonly byte[] DefaultPalette = _GenerateDefaultPalette();

  private static byte[] _GenerateDefaultPalette() {
    var pal = new byte[768];
    for (var i = 0; i < 256; ++i) {
      pal[i * 3] = (byte)i;
      pal[i * 3 + 1] = (byte)i;
      pal[i * 3 + 2] = (byte)i;
    }
    return pal;
  }

  /// <summary>Converts a Quake sprite to a <see cref="RawImage"/>. Returns Indexed8 with embedded palette.</summary>
  public static RawImage ToRawImage(QuakeSprFile file) {

    var palette = file.Palette;
    var paletteCount = palette.Length / 3;

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = file.PixelData[..],
      Palette = palette[..],
      PaletteCount = paletteCount,
    };
  }

  /// <summary>Creates a Quake sprite from a <see cref="RawImage"/>. Must be Indexed8.</summary>
  public static QuakeSprFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed8)
      throw new ArgumentException($"Quake SPR requires Indexed8 pixel format, got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = image.Palette != null ? image.Palette[..] : DefaultPalette,
    };
  }
}
