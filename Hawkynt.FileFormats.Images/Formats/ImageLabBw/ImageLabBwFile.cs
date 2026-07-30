using System;
using FileFormat.Core;

namespace FileFormat.ImageLabBw;

/// <summary>In-memory representation of an ImageLab greyscale picture (.b&amp;w, .b_w).</summary>
/// <remarks>
/// About as plain as a format gets: a six-byte signature, the dimensions as big-endian sixteen-bit
/// values, and then one byte of grey per pixel. No palette, no compression, no rows padded to
/// anything.
/// <para/>
/// The two extensions are the same format under two spellings — <c>.b&amp;w</c> where the filesystem
/// allowed an ampersand and <c>.b_w</c> where it did not.
/// </remarks>
public readonly record struct ImageLabBwFile
  : IImageFormatReader<ImageLabBwFile>, IImageToRawImage<ImageLabBwFile>,
    IImageFromRawImage<ImageLabBwFile>, IImageFormatWriter<ImageLabBwFile> {

  /// <summary>The bytes every file starts with.</summary>
  public static ReadOnlySpan<byte> Magic => "B&W256"u8;

  /// <summary>Size of the header: the signature then two big-endian dimensions.</summary>
  public const int HeaderSize = 10;

  /// <summary>Largest picture we accept, guarding against a corrupt header claiming gigabytes.</summary>
  public const int MaxDimension = 4096;

  static string IImageFormatMetadata<ImageLabBwFile>.PrimaryExtension => ".b&w";
  static string[] IImageFormatMetadata<ImageLabBwFile>.FileExtensions => [".b&w", ".b_w"];
  static ImageLabBwFile IImageFormatReader<ImageLabBwFile>.FromSpan(ReadOnlySpan<byte> data) => ImageLabBwReader.FromSpan(data);
  static byte[] IImageFormatWriter<ImageLabBwFile>.ToBytes(ImageLabBwFile file) => ImageLabBwWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ImageLabBwFile>.VideoModes => [
    new("Greyscale", [(IntegerRange.Any, IntegerRange.Any)], [256])
  ];

  /// <summary>Picture width.</summary>
  public int Width { get; init; }

  /// <summary>Picture height.</summary>
  public int Height { get; init; }

  /// <summary>One byte of grey per pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The 256 greys, which are simply every level repeated across all three channels.</summary>
  internal static byte[] GreyscalePalette() {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i)
      palette[i * 3] = palette[i * 3 + 1] = palette[i * 3 + 2] = (byte)i;

    return palette;
  }

  public static RawImage ToRawImage(ImageLabBwFile file) {
    var expected = file.Width * file.Height;
    var pixels = new byte[expected];
    var data = file.PixelData ?? [];
    data.AsSpan(0, Math.Min(data.Length, expected)).CopyTo(pixels);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = GreyscalePalette(),
      PaletteCount = 256,
    };
  }

  public static ImageLabBwFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width < 1 || image.Height < 1 || image.Width > MaxDimension || image.Height > MaxDimension)
      throw new ArgumentException($"A greyscale picture is at most {MaxDimension}x{MaxDimension}, got {image.Width}x{image.Height}.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);
    var data = new byte[image.Width * image.Height];

    // Rec. 601 luma, which is what a greyscale conversion means when the source has colour.
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)((rgb.PixelData[i * 3] * 299 + rgb.PixelData[i * 3 + 1] * 587 + rgb.PixelData[i * 3 + 2] * 114) / 1000);

    return new() { Width = image.Width, Height = image.Height, PixelData = data };
  }
}
