using System;
using FileFormat.Core;

namespace FileFormat.HereticM8;

/// <summary>In-memory representation of a Heretic II mipmap texture (.m8).</summary>
/// <remarks>
/// This used to take bytes 0 to 5 as a size and substitute a default when they looked wrong, which
/// for these files gave 2 pixels by 256 and, for one of them, 2 by 27507 — the version number and
/// whatever followed it read as a width and a height.
/// <para/>
/// The real layout: a version of 2, a name of 32 bytes, then sixteen widths, sixteen heights and
/// sixteen offsets — one set per mipmap level, unused levels being nought. After those a second name,
/// a palette of 256 colours, and three more long words, which puts the first level's pixels at 1040.
/// The arithmetic settles it: 1040 plus 256 by 256 is 66576, which is one of the samples to the byte.
/// <para/>
/// The levels are the same picture at halving sizes, so the first is the one to read.
/// </remarks>
public readonly record struct HereticM8File
  : IImageFormatReader<HereticM8File>, IImageToRawImage<HereticM8File>,
    IImageFromRawImage<HereticM8File>, IImageFormatWriter<HereticM8File> {

  static string IImageFormatMetadata<HereticM8File>.PrimaryExtension => ".m8";
  static string[] IImageFormatMetadata<HereticM8File>.FileExtensions => [".m8"];
  static HereticM8File IImageFormatReader<HereticM8File>.FromSpan(ReadOnlySpan<byte> data) => HereticM8Reader.FromSpan(data);
  static byte[] IImageFormatWriter<HereticM8File>.ToBytes(HereticM8File file) => HereticM8Writer.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<HereticM8File>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>The version every file states.</summary>
  internal const int Version = 2;

  /// <summary>Mipmap levels a file has room for.</summary>
  internal const int Levels = 16;

  /// <summary>Where the widths, heights and offsets begin.</summary>
  internal const int WidthsOffset = 4 + 32;

  /// <summary>Where the palette begins: after the tables and a second name.</summary>
  internal const int PaletteOffset = WidthsOffset + Levels * 4 * 3 + 32;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>One index per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The 256 colours the file states, as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(HereticM8File file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = file.Palette[..],
    PaletteCount = 256,
  };

  /// <summary>Builds a texture, quantised onto the 256 colours the format keeps a table of.</summary>
  /// <remarks>
  /// Only the first mipmap level is written. The rest are the same picture at halving sizes, and a
  /// file that states none of them is still a file this reader takes — it reads level zero and no
  /// other, because that is the full-size picture.
  /// </remarks>
  public static HereticM8File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = PixelConverter.Convert(image, PixelFormat.Indexed8);

    var palette = new byte[256 * 3];
    (image.Palette ?? []).AsSpan(0, Math.Min(image.Palette?.Length ?? 0, palette.Length)).CopyTo(palette);

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
      Palette = palette,
    };
  }
}
