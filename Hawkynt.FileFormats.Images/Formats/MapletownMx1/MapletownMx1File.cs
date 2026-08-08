using System;
using FileFormat.Core;
using FileFormat.Mapletown;

namespace FileFormat.MapletownMx1;

/// <summary>In-memory representation of a Mapletown Network MX1 picture (.mx1).</summary>
/// <remarks>
/// The same drawing format as ML1 written as printable characters, six bits to each, so that it
/// could be posted to a bulletin board. A file may hold several images, announced by a marked line
/// apiece, and what they add up to depends on their sizes: four of one size are a two-by-two grid
/// and sixteen are four-by-four, while anything else is stacked one above the next.
/// </remarks>
public readonly record struct MapletownMx1File
  : IImageFormatReader<MapletownMx1File>, IImageToRawImage<MapletownMx1File>,
    IImageFromRawImage<MapletownMx1File>, IImageFormatWriter<MapletownMx1File> {

  static string IImageFormatMetadata<MapletownMx1File>.PrimaryExtension => ".mx1";
  static string[] IImageFormatMetadata<MapletownMx1File>.FileExtensions => [".mx1"];
  static MapletownMx1File IImageFormatReader<MapletownMx1File>.FromSpan(ReadOnlySpan<byte> data)
    => MapletownMx1Reader.FromSpan(data);
  static byte[] IImageFormatWriter<MapletownMx1File>.ToBytes(MapletownMx1File file)
    => MapletownMx1Writer.ToBytes(file);
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

  /// <summary>Widest and tallest a picture may be: the corners are stated in sixteen bits each.</summary>
  public const int MaxDimension = 65536;

  /// <summary>
  /// Most pixels a picture may hold: the end of one is announced as a length one past its pixel
  /// count, and a length is twenty-one bits at the widest.
  /// </summary>
  public const int MaxPixels = MapletownEncoder.MaxLength - 1;

  /// <summary>
  /// Encodes a picture as a single MX1 image, reduced to the 128 colours a palette holds.
  /// </summary>
  /// <remarks>
  /// The format has no size of its own, so a picture keeps the one it came with — up to the two
  /// places the stream itself cannot follow it. A corner is sixteen bits, and the marker that ends
  /// an image is a length, which stops at twenty-one; a picture past either is sampled down to fit
  /// rather than refused, since neither limit is anything a viewer would have shown.
  /// <para/>
  /// A colour is a number in base nine with a digit a channel, so the reduction is to those levels
  /// first and to 128 of them second. Runs are taken across the whole picture rather than a row at
  /// a time, because the reader scans it as one line of pixels and a run that reaches the end of a
  /// row simply continues on the next.
  /// </remarks>
  public static MapletownMx1File FromRawImage(RawImage image) {
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
    var (colors, indices) = MapletownMx1Writer.Reduce(rgb, width * height);

    var pixels = new byte[width * height * 3];
    for (var pixel = 0; pixel < indices.Length; ++pixel) {
      var (red, green, blue) = MapletownMx1Writer.Expand(colors[indices[pixel]]);
      pixels[pixel * 3] = red;
      pixels[pixel * 3 + 1] = green;
      pixels[pixel * 3 + 2] = blue;
    }

    return new() { Width = width, Height = height, Pixels = pixels };
  }
}
