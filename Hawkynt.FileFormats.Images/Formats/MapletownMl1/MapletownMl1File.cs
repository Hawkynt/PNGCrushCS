using System;
using FileFormat.Core;
using FileFormat.Mapletown;

namespace FileFormat.MapletownMl1;

/// <summary>In-memory representation of a Mapletown Network ML1 picture (.ml1).</summary>
/// <remarks>
/// A drawing rather than a photograph, and stored as one: horizontal runs of colour, with a
/// separate kind of stroke — a chain — that walks down the picture ahead of the scan to lay an
/// outline the runs then stop at. Nothing about it is a bitmap.
/// </remarks>
public readonly record struct MapletownMl1File
  : IImageFormatReader<MapletownMl1File>, IImageToRawImage<MapletownMl1File> {

  static string IImageFormatMetadata<MapletownMl1File>.PrimaryExtension => ".ml1";
  static string[] IImageFormatMetadata<MapletownMl1File>.FileExtensions => [".ml1"];
  static MapletownMl1File IImageFormatReader<MapletownMl1File>.FromSpan(ReadOnlySpan<byte> data)
    => MapletownMl1Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MapletownMl1File>.VideoModes => [
    new("NEC PC-98", [(IntegerRange.Any, IntegerRange.Any)], [729])
  ];

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The decoded picture, three bytes a pixel.</summary>
  public byte[] Pixels { get; init; }

  public static RawImage ToRawImage(MapletownMl1File file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.Pixels ?? new byte[file.Width * file.Height * 3],
  };
}
