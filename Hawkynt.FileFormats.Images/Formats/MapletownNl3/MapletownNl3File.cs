using System;
using FileFormat.Core;

namespace FileFormat.MapletownNl3;

/// <summary>In-memory representation of a Mapletown Network NL3 picture (.nl3).</summary>
/// <remarks>
/// A picture written to survive a bulletin board: every byte of it is a printable character, so the
/// file can be pasted into a message and read back out. That constraint shapes everything — the
/// values run 0 to 160 rather than 0 to 255 because that is how many usable characters there were,
/// a palette entry needs two of them, and a few characters are stored as the multi-byte sequences a
/// Japanese board would pass through unaltered.
/// <para/>
/// The picture is stored column by column, which is how the terminal drew it.
/// </remarks>
public readonly record struct MapletownNl3File
  : IImageFormatReader<MapletownNl3File>, IImageToRawImage<MapletownNl3File> {

  /// <summary>Pixels across.</summary>
  public const int Width = 160;

  /// <summary>Rows.</summary>
  public const int Height = 100;

  /// <summary>Colours the palette holds.</summary>
  public const int ColorCount = 64;

  static string IImageFormatMetadata<MapletownNl3File>.PrimaryExtension => ".nl3";
  static string[] IImageFormatMetadata<MapletownNl3File>.FileExtensions => [".nl3"];
  static MapletownNl3File IImageFormatReader<MapletownNl3File>.FromSpan(ReadOnlySpan<byte> data)
    => MapletownNl3Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MapletownNl3File>.VideoModes => [
    new("NEC PC-98", [(Width, Height)], [ColorCount])
  ];

  /// <summary>One palette index per pixel.</summary>
  public byte[] Pixels { get; init; }

  /// <summary>Sixty-four RGB triplets.</summary>
  public byte[] Palette { get; init; }

  public static RawImage ToRawImage(MapletownNl3File file) => new() {
    Width = Width,
    Height = Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.Pixels ?? new byte[Width * Height],
    Palette = file.Palette ?? new byte[ColorCount * 3],
    PaletteCount = ColorCount,
  };
}
