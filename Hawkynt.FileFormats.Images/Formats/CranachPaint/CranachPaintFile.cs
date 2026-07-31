using System;
using FileFormat.Core;

namespace FileFormat.CranachPaint;

/// <summary>In-memory representation of a TmS Cranach Paint picture (.esm).</summary>
/// <remarks>
/// A Falcon picture at one, eight or twenty-four bits a pixel behind an 812-byte header, almost all
/// of which is the palette — 256 colours stored as three separate planes of 256 bytes each rather
/// than as triplets, which is the order the hardware's three colour registers are loaded in.
/// <para/>
/// The palette is present whatever the depth, so a monochrome or true-colour picture carries three
/// quarters of a kilobyte it never reads.
/// </remarks>
public readonly record struct CranachPaintFile
  : IImageFormatReader<CranachPaintFile>, IImageToRawImage<CranachPaintFile> {

  /// <summary>The text every file starts with.</summary>
  public const string Signature = "TMS";

  /// <summary>Offset of the palette's red plane; green and blue follow at 256-byte intervals.</summary>
  public const int PaletteOffset = 36;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 256;

  /// <summary>Offset of the pixels.</summary>
  public const int PixelsOffset = 812;

  static string IImageFormatMetadata<CranachPaintFile>.PrimaryExtension => ".esm";
  static string[] IImageFormatMetadata<CranachPaintFile>.FileExtensions => [".esm"];
  static CranachPaintFile IImageFormatReader<CranachPaintFile>.FromSpan(ReadOnlySpan<byte> data)
    => CranachPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<CranachPaintFile>.VideoModes => [
    new("Cranach", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>The whole file.</summary>
  public byte[] Data { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Bits a pixel occupies: 1, 8 or 24.</summary>
  public int Depth { get; init; }

  public static RawImage ToRawImage(CranachPaintFile file) {
    var data = file.Data ?? [];
    var count = file.Width * file.Height;

    switch (file.Depth) {
      case 1: {
        var stride = (file.Width + 7) >> 3;
        var pixels = new byte[count];
        for (var y = 0; y < file.Height; ++y)
        for (var x = 0; x < file.Width; ++x) {
          var at = PixelsOffset + y * stride + (x >> 3);
          if (at < data.Length && ((data[at] >> (~x & 7)) & 1) != 0)
            pixels[y * file.Width + x] = 1;
        }

        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Indexed8,
          PixelData = pixels,
          Palette = [255, 255, 255, 0, 0, 0],
          PaletteCount = 2,
        };
      }

      case 8: {
        // Three planes, one per channel, each holding every colour's value for that channel.
        var palette = new byte[ColorCount * 3];
        for (var i = 0; i < ColorCount; ++i)
        for (var channel = 0; channel < 3; ++channel)
          palette[i * 3 + channel] = data[PaletteOffset + channel * ColorCount + i];

        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Indexed8,
          PixelData = data[PixelsOffset..(PixelsOffset + count)],
          Palette = palette,
          PaletteCount = ColorCount,
        };
      }

      default:
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Rgb24,
          PixelData = data[PixelsOffset..(PixelsOffset + count * 3)],
        };
    }
  }
}
