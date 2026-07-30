using System;
using FileFormat.Core;

namespace FileFormat.GraphSaurus6;

/// <summary>In-memory representation of a Graph Saurus Screen 6 picture (.sr6).</summary>
/// <remarks>
/// A BSAVE header and then a Screen 6 bitmap, two bits a pixel across 512. Unlike the fixed-size
/// <c>.sc6</c> files, the height comes from the header's end address, so a picture can be shorter
/// than a full screen — Graph Saurus saved whatever the drawing occupied rather than padding it out.
/// <para/>
/// Screen 6 puts 512 pixels on a line by halving the vertical resolution, so every stored row is
/// drawn on two scanlines. The palette belongs to a companion <c>.PL6</c>; without one the picture
/// means what the machine starts up showing, which is black and three greens.
/// </remarks>
[FormatMagicBytes([0xFE])]
public readonly record struct GraphSaurus6File
  : IImageFormatReader<GraphSaurus6File>, IImageToRawImage<GraphSaurus6File> {

  /// <summary>Stored pixels per row.</summary>
  public const int Width = 512;

  /// <summary>Bytes one stored row occupies, at four pixels per byte.</summary>
  public const int BytesPerRow = Width / 4;

  /// <summary>Offset of the bitmap, after the BSAVE header.</summary>
  public const int BitmapOffset = MsxGraphics.BsaveHeaderSize;

  /// <summary>Colours a Screen 6 picture can show at once.</summary>
  public const int ColorCount = 4;

  /// <summary>Tallest picture the mode displays.</summary>
  public const int MaxHeight = 212;

  static string IImageFormatMetadata<GraphSaurus6File>.PrimaryExtension => ".sr6";
  static string[] IImageFormatMetadata<GraphSaurus6File>.FileExtensions => [".sr6"];
  static GraphSaurus6File IImageFormatReader<GraphSaurus6File>.FromSpan(ReadOnlySpan<byte> data)
    => GraphSaurus6Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<GraphSaurus6File>.VideoModes => [
    new("Screen 6", [(Width, IntegerRange.Any)], [ColorCount])
  ];

  /// <summary>Stored rows; the picture is drawn twice as tall.</summary>
  public int StoredHeight { get; init; }

  /// <summary>The bitmap, four pixels per byte, most significant pair leftmost.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(GraphSaurus6File file) {
    var data = file.PixelData ?? [];
    var height = file.StoredHeight * 2;
    var pixels = new byte[Width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < Width; ++x) {
      // A linear pixel index, four to a byte, so a row is 128 bytes.
      var index = (y >> 1) * Width + x;
      var b = index >> 2 < data.Length ? data[index >> 2] : 0;
      pixels[y * Width + x] = (byte)((b >> ((~index & 3) << 1)) & 3);
    }

    return new() {
      Width = Width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = MsxGraphics.Screen6DefaultPaletteRgb.ToArray(),
      PaletteCount = ColorCount,
    };
  }
}
