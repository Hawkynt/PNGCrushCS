using System;
using FileFormat.Core;

namespace FileFormat.WhyPic;

/// <summary>In-memory representation of a WhyPic (.ypc) picture.</summary>
/// <remarks>
/// Osamu Yamaji's 1998 lossless compressor for 16- and 256-colour bitmaps. Two letters,
/// <c>Yp</c>, and then nothing but one arithmetic code: the size, the version, the colour count and
/// the palette are coded as evenly-spread values, the palette optionally against its own byte
/// statistics, and every pixel against a mixture of eight context models whose interpolation
/// weights the encoder fits to the picture and stores in front of the pixels.
/// <para/>
/// Because no field sits on a byte boundary after the first two, the header cannot be read without
/// running the decoder, and a file says nothing about how long it is: the end is wherever the last
/// pixel's interval closes.
/// </remarks>
public readonly record struct WhyPicFile
  : IImageFormatReader<WhyPicFile>, IImageToRawImage<WhyPicFile>,
    IImageFromRawImage<WhyPicFile>, IImageFormatWriter<WhyPicFile>, IImageInfoReader<WhyPicFile> {

  /// <summary>The largest width or height the twelve-bit size fields hold.</summary>
  public const int MaxDimension = 4095;

  static string IImageFormatMetadata<WhyPicFile>.PrimaryExtension => ".ypc";
  static string[] IImageFormatMetadata<WhyPicFile>.FileExtensions => [".ypc"];
  static WhyPicFile IImageFormatReader<WhyPicFile>.FromSpan(ReadOnlySpan<byte> data) => WhyPicReader.FromSpan(data);
  static byte[] IImageFormatWriter<WhyPicFile>.ToBytes(WhyPicFile file) => WhyPicWriter.ToBytes(file);
  static bool? IImageFormatMetadata<WhyPicFile>.MatchesSignature(ReadOnlySpan<byte> header) => WhyPicReader.ReadHeader(header) != null;
  static VideoMode[] IImageFormatMetadata<WhyPicFile>.VideoModes => [
    new("Default", [(new IntegerRange(1, MaxDimension), new IntegerRange(1, MaxDimension))], [new IntegerRange(2, 256)])
  ];

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>16 or 256: the only two colour counts the format names.</summary>
  public int Colors { get; init; }

  /// <summary>Which version's coding the pixels use.</summary>
  public WhyPicLayout Layout { get; init; }

  /// <summary>Whether the palette was coded against its own statistics rather than as plain bytes.</summary>
  /// <remarks>Reported by the reader; the writer makes the choice afresh, taking whichever is shorter, as WhyPic does.</remarks>
  public bool PaletteCompressed { get; init; }

  /// <summary><see cref="Colors"/> entries of red, green and blue.</summary>
  public byte[] Palette { get; init; }

  /// <summary>One palette index per pixel, top row first.</summary>
  public byte[] PixelData { get; init; }

  public static ImageInfo? ReadImageInfo(ReadOnlySpan<byte> header) {
    var parsed = WhyPicReader.ReadHeader(header);
    if (parsed is not { } h)
      return null;

    return new(h.Width, h.Height, h.Colors == 16 ? 4 : 8, "Indexed",
      h.Layout == WhyPicLayout.Version11 ? "WhyPic 1.1" : "WhyPic 1.0");
  }

  public static RawImage ToRawImage(WhyPicFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = file.Palette[..],
    PaletteCount = file.Colors,
  };

  /// <summary>Reduces the picture to at most 256 colours and stores it in version 1.1's coding.</summary>
  /// <remarks>Sixteen colours or fewer are written as a 16-colour picture, which is what the format was made for.</remarks>
  public static WhyPicFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1 || image.Width > MaxDimension || image.Height > MaxDimension)
      throw new ArgumentException($"WhyPic holds pictures from 1x1 to {MaxDimension}x{MaxDimension}, not {image.Width}x{image.Height}.", nameof(image));

    var indexed = image.EnsureIndexedAtMost(256);
    var colors = indexed.PaletteCount <= 16 ? 16 : 256;
    var palette = new byte[colors * 3];
    var source = indexed.Palette ?? [];
    source.AsSpan(0, Math.Min(source.Length, Math.Min(indexed.PaletteCount, colors) * 3)).CopyTo(palette);

    var pixels = new byte[image.Width * image.Height];
    indexed.PixelData.AsSpan(0, pixels.Length).CopyTo(pixels);
    for (var i = 0; i < pixels.Length; ++i)
      if (pixels[i] >= colors)
        throw new ArgumentException($"Pixel {i} names palette entry {pixels[i]} of {colors}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Colors = colors,
      Layout = WhyPicLayout.Version11,
      Palette = palette,
      PixelData = pixels,
    };
  }
}
