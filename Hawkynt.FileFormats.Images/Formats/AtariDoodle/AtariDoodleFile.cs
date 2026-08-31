using System;
using FileFormat.Core;

namespace FileFormat.AtariDoodle;

/// <summary>In-memory representation of an original Atari ST Doodle (.DOO) high-resolution screen dump.</summary>
/// <remarks>
/// The original DR Doodle format is exactly one 640x400 monochrome Atari ST screen: no header,
/// palette, dimensions, or compression. Later software reused .DOO for low/medium-resolution screen
/// dumps, but those files contain no mode or palette metadata and are therefore not auto-guessed.
/// </remarks>
[FormatDetectionPriority(10)]
public readonly record struct AtariDoodleFile : IImageFormatReader<AtariDoodleFile>, IImageToRawImage<AtariDoodleFile>, IImageFromRawImage<AtariDoodleFile>, IImageFormatWriter<AtariDoodleFile> {

  /// <summary>Fixed image width in pixels.</summary>
  public const int FixedWidth = 640;

  /// <summary>Fixed image height in pixels.</summary>
  public const int FixedHeight = 400;

  /// <summary>Bytes occupied by one high-resolution Atari ST screen.</summary>
  public const int ScreenDataSize = 32_000;

  static string IImageFormatMetadata<AtariDoodleFile>.PrimaryExtension => ".doo";
  static string[] IImageFormatMetadata<AtariDoodleFile>.FileExtensions => [".doo"];
  static AtariDoodleFile IImageFormatReader<AtariDoodleFile>.FromSpan(ReadOnlySpan<byte> data) => AtariDoodleReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariDoodleFile>.VideoModes => [new("Atari ST high resolution", [(FixedWidth, FixedHeight)], [2])];
  static byte[] IImageFormatWriter<AtariDoodleFile>.ToBytes(AtariDoodleFile file) => AtariDoodleWriter.ToBytes(file);

  /// <summary>Image width, always 640.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 400.</summary>
  public int Height => FixedHeight;

  /// <summary>Exactly 32,000 bytes of Atari ST one-plane high-resolution screen memory.</summary>
  public byte[] ScreenData { get; init; }

  /// <summary>Converts the raw ST screen into an indexed black-and-white image.</summary>
  public static RawImage ToRawImage(AtariDoodleFile file) {
    Validate(file, nameof(file));
    return new RawImage {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed8,
      PixelData = PlanarConverter.AtariStToChunky(file.ScreenData, FixedWidth, FixedHeight, 1),
      Palette = AtariStGraphics.MonochromePalette(),
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a Doodle screen from an exactly 640x400 image using a mid-grey luminance threshold.</summary>
  public static AtariDoodleFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Atari ST Doodle images must be exactly {FixedWidth}x{FixedHeight} pixels.", nameof(image));

    image = image.EnsureAnyFormat(PixelFormat.Rgb24);
    var indices = new byte[FixedWidth * FixedHeight];
    for (var i = 0; i < indices.Length; ++i) {
      var source = i * 3;
      var r = image.PixelData[source];
      var g = image.PixelData[source + 1];
      var b = image.PixelData[source + 2];
      var luma = (299 * r + 587 * g + 114 * b + 500) / 1000;
      indices[i] = luma < 128 ? (byte)1 : (byte)0;
    }

    return new AtariDoodleFile {
      ScreenData = PlanarConverter.ChunkyToAtariSt(indices, FixedWidth, FixedHeight, 1),
    };
  }

  internal static void Validate(AtariDoodleFile file, string parameterName) {
    if (file.ScreenData is null || file.ScreenData.Length != ScreenDataSize)
      throw new ArgumentException($"Atari ST Doodle screen memory must contain exactly {ScreenDataSize} bytes.", parameterName);
  }
}
