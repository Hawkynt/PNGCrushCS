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
  : IImageFormatReader<MapletownMl1File>, IImageToRawImage<MapletownMl1File>,
    IImageFromRawImage<MapletownMl1File>, IImageFormatWriter<MapletownMl1File> {

  static string IImageFormatMetadata<MapletownMl1File>.PrimaryExtension => ".ml1";
  static string[] IImageFormatMetadata<MapletownMl1File>.FileExtensions => [".ml1"];
  static MapletownMl1File IImageFormatReader<MapletownMl1File>.FromSpan(ReadOnlySpan<byte> data)
    => MapletownMl1Reader.FromSpan(data);
  static byte[] IImageFormatWriter<MapletownMl1File>.ToBytes(MapletownMl1File file)
    => MapletownMl1Writer.ToBytes(file);
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

  /// <summary>Widest and tallest a picture may be: the corners are stated in sixteen bits each.</summary>
  public const int MaxDimension = MapletownPicture.MaxDimension;

  /// <summary>
  /// Most pixels a picture may hold: the end of one is announced as a length one past its pixel
  /// count, and a length is twenty-one bits at the widest.
  /// </summary>
  public const int MaxPixels = MapletownPicture.MaxPixels;

  /// <summary>
  /// Encodes a picture as one ML1 image, reduced to the 128 colours a palette holds.
  /// </summary>
  /// <remarks>
  /// The format has no size of its own, so a picture keeps the one it came with — up to the two
  /// places the stream itself cannot follow it. A corner is sixteen bits, and the marker that ends
  /// an image is a length, which stops at twenty-one; a picture past either is sampled down to fit
  /// rather than refused, since neither limit is anything a viewer would have shown, and MX1 — the
  /// same image in printable characters — already answers the question that way.
  /// <para/>
  /// A colour is a number in base nine with a digit a channel, so the reduction is to those levels
  /// first and to 128 of them second. What comes back out of <see cref="ToRawImage"/> afterwards is
  /// exactly what the file holds, so a caller can see what was kept before writing it.
  /// </remarks>
  public static MapletownMl1File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = Math.Clamp(image.Width, 1, MaxDimension);
    var height = Math.Clamp(image.Height, 1, MaxDimension);
    if ((long)width * height > MaxPixels) {
      var scale = Math.Sqrt(MaxPixels / ((double)width * height));
      width = Math.Max(1, (int)(width * scale));
      height = Math.Max(1, (int)(height * scale));
      while ((long)width * height > MaxPixels)
        if (width >= height)
          --width;
        else
          --height;
    }

    var rgb = image.SampleTo(width, height).PixelData;
    var (colors, indices) = MapletownPicture.Reduce(rgb, width * height);

    var pixels = new byte[width * height * 3];
    for (var pixel = 0; pixel < indices.Length; ++pixel) {
      var (red, green, blue) = MapletownPicture.Expand(colors[indices[pixel]]);
      pixels[pixel * 3] = red;
      pixels[pixel * 3 + 1] = green;
      pixels[pixel * 3 + 2] = blue;
    }

    return new() { Width = width, Height = height, Pixels = pixels };
  }
}
