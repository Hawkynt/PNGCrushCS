using System;
using FileFormat.Core;

namespace FileFormat.Kitty;

/// <summary>In-memory representation of a Kitty picture (.kty, .kt4).</summary>
/// <remarks>
/// A PC-88 VA format built entirely out of a four-pixel tile and where to put it. A block names one
/// tile and then lists the rectangles and the single positions it fills; the picture is the
/// accumulation of those blocks, and whatever is still blank when the list ends is filled from the
/// remaining bytes in scan order. Nothing is ever a bitmap.
/// <para/>
/// Each channel is one bit, so a tile is three bytes for eight rows of colour — or six, in the mode
/// that gives a tile four rows instead of two.
/// </remarks>
public readonly record struct KittyFile
  : IImageFormatReader<KittyFile>, IImageToRawImage<KittyFile>,
    IImageFromRawImage<KittyFile>, IImageFormatWriter<KittyFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = 640;

  /// <summary>Rows.</summary>
  public const int Height = 400;

  /// <summary>Tiles across.</summary>
  public const int Columns = 160;

  /// <summary>Tile rows.</summary>
  public const int Rows = 100;

  /// <summary>Pixels one tile covers across.</summary>
  public const int TileWidth = 4;

  static string IImageFormatMetadata<KittyFile>.PrimaryExtension => ".kty";
  static string[] IImageFormatMetadata<KittyFile>.FileExtensions => [".kty", ".kt4"];
  static KittyFile IImageFormatReader<KittyFile>.FromSpan(ReadOnlySpan<byte> data)
    => KittyReader.FromSpan(data);
  static byte[] IImageFormatWriter<KittyFile>.ToBytes(KittyFile file) => KittyWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<KittyFile>.VideoModes => [
    new("PC-88 VA", [(Width, Height)], [8])
  ];

  /// <summary>The decoded picture, three bytes a pixel.</summary>
  public byte[] Pixels { get; init; }

  public static RawImage ToRawImage(KittyFile file) => new() {
    Width = Width,
    Height = Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.Pixels ?? new byte[Width * Height * 3],
  };

  /// <summary>Builds a picture from an image, which is scaled to the machine's fixed screen.</summary>
  /// <remarks>
  /// The size is not negotiable — the format has no field for it — so a picture of any other size
  /// is stretched to fit rather than refused. Colour is one bit a channel, so the eight corners of
  /// the colour cube are all there is; a picture already using only those comes back unchanged.
  /// </remarks>
  public static KittyFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException("A picture needs at least one pixel.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var pixels = new byte[Width * Height * 3];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var sourceX = image.Width == Width ? x : x * image.Width / Width;
      var sourceY = image.Height == Height ? y : y * image.Height / Height;
      var source = (sourceY * image.Width + sourceX) * 3;
      var target = (y * Width + x) * 3;

      pixels[target] = rgb.PixelData[source];
      pixels[target + 1] = rgb.PixelData[source + 1];
      pixels[target + 2] = rgb.PixelData[source + 2];
    }

    return new() { Pixels = pixels };
  }
}
