using System;
using FileFormat.Core;

namespace FileFormat.MapletownMx1;

/// <summary>In-memory representation of a Mapletown Network MX1 picture (.mx1).</summary>
/// <remarks>
/// The same drawing format as ML1 written as printable characters, six bits to each, so that it
/// could be posted to a bulletin board. A file may hold several images, announced by a marked line
/// apiece, and what they add up to depends on their sizes: four of one size are a two-by-two grid
/// and sixteen are four-by-four, while anything else is stacked one above the next.
/// </remarks>
public readonly record struct MapletownMx1File
  : IImageFormatReader<MapletownMx1File>, IImageToRawImage<MapletownMx1File> {

  static string IImageFormatMetadata<MapletownMx1File>.PrimaryExtension => ".mx1";
  static string[] IImageFormatMetadata<MapletownMx1File>.FileExtensions => [".mx1"];
  static MapletownMx1File IImageFormatReader<MapletownMx1File>.FromSpan(ReadOnlySpan<byte> data)
    => MapletownMx1Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MapletownMx1File>.VideoModes => [
    new("NEC PC-98", [(IntegerRange.Any, IntegerRange.Any)], [729])
  ];

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The decoded picture, three bytes a pixel.</summary>
  public byte[] Pixels { get; init; }

  public static RawImage ToRawImage(MapletownMx1File file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.Pixels ?? new byte[file.Width * file.Height * 3],
  };
}
