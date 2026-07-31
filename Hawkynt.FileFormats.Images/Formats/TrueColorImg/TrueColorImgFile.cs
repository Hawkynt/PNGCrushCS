using System;
using FileFormat.Core;

namespace FileFormat.TrueColorImg;

/// <summary>In-memory representation of a true-colour GEM bit image (.timg).</summary>
/// <remarks>
/// GEM's IMG format was designed for one bit a pixel and grew colour by adding bitplanes, which is
/// why a true-colour version of it is so strange: twenty-four bitplanes, one per bit of the colour,
/// packed and unpacked exactly as if they were still black and white. A run of sky is a run in
/// every one of the twenty-four planes at once.
/// <para/>
/// One variant gives up on that and stores whole pixels instead, three bytes each behind a repeat
/// marker. It is the only part of the format that looks like a true-colour format.
/// </remarks>
public readonly record struct TrueColorImgFile
  : IImageFormatReader<TrueColorImgFile>, IImageToRawImage<TrueColorImgFile>,
    IImageFromRawImage<TrueColorImgFile>, IImageFormatWriter<TrueColorImgFile> {

  static string IImageFormatMetadata<TrueColorImgFile>.PrimaryExtension => ".timg";
  static string[] IImageFormatMetadata<TrueColorImgFile>.FileExtensions => [".timg"];
  static TrueColorImgFile IImageFormatReader<TrueColorImgFile>.FromSpan(ReadOnlySpan<byte> data)
    => TrueColorImgReader.FromSpan(data);
  static byte[] IImageFormatWriter<TrueColorImgFile>.ToBytes(TrueColorImgFile file) => TrueColorImgWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<TrueColorImgFile>.VideoModes => [
    new("Atari Falcon", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>The decoded picture, three bytes a pixel.</summary>
  public byte[] Pixels { get; init; }

  public static RawImage ToRawImage(TrueColorImgFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.Pixels ?? new byte[file.Width * file.Height * 3],
  };

  /// <summary>Builds a picture from an image, which needs no reduction at all.</summary>
  /// <remarks>
  /// The chunky variant stores three whole bytes a pixel, so this is the rare case of a vintage
  /// format that can hold anything a modern one can and needs no palette, no dither and no choice.
  /// </remarks>
  public static TrueColorImgFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > 65535 || image.Height is < 1 or > 65535)
      throw new ArgumentException($"A picture is at most 65535 each way, got {image.Width}x{image.Height}.", nameof(image));

    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);

    return new() { Width = image.Width, Height = image.Height, Pixels = rgb.PixelData[..] };
  }
}
