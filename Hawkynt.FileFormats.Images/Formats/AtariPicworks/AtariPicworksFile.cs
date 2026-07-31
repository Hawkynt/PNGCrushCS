using System;
using FileFormat.Core;

namespace FileFormat.AtariPicworks;

/// <summary>In-memory representation of a Picworks picture (.cp3).</summary>
/// <remarks>
/// A packed Doodle screen — the Atari ST's 640x400 monochrome mode, which is one bit a pixel and
/// exactly 32000 bytes. The packing works in eight-byte units rather than single bytes, alternating
/// between a run copied verbatim and a run of one eight-byte group repeated. Eight bytes is a
/// character cell's worth of a monochrome screen, so the unit is what a page of text or a flat area
/// of a drawing actually repeats.
/// <para/>
/// The counts are gathered at the front of the file in pairs and the bytes they refer to follow, so
/// the two streams are read at different speeds rather than interleaved.
/// </remarks>
public readonly record struct AtariPicworksFile
  : IImageFormatReader<AtariPicworksFile>, IImageToRawImage<AtariPicworksFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 640;

  /// <summary>Rows.</summary>
  public const int Height = 400;

  /// <summary>Size of the screen the file unpacks to.</summary>
  public const int ScreenSize = Width / 8 * Height;

  /// <summary>Bytes the packing works in.</summary>
  public const int GroupSize = 8;

  static string IImageFormatMetadata<AtariPicworksFile>.PrimaryExtension => ".cp3";
  static string[] IImageFormatMetadata<AtariPicworksFile>.FileExtensions => [".cp3"];
  static AtariPicworksFile IImageFormatReader<AtariPicworksFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariPicworksReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariPicworksFile>.VideoModes => [
    new("Doodle", [(Width, Height)], [2])
  ];

  /// <summary>The unpacked screen.</summary>
  public byte[] ScreenData { get; init; }

  public static RawImage ToRawImage(AtariPicworksFile file) {
    var screen = file.ScreenData ?? [];
    var stride = Width / 8;
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = y * stride + (x >> 3);
      if (at < screen.Length && ((screen[at] >> (~x & 7)) & 1) != 0)
        pixels[y * Width + x] = 1;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [255, 255, 255, 0, 0, 0],
      PaletteCount = 2,
    };
  }
}
