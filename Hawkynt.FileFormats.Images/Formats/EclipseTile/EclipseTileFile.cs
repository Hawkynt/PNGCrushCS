using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.EclipseTile;

/// <summary>In-memory representation of an Eclipse tiled raster (.tile).</summary>
/// <remarks>
/// A prepress raster written by Alias's Eclipse: four kilobytes of header, then the picture in fixed
/// 256 by 256 tiles, uncompressed, four bytes to a pixel whatever the number of channels.
/// <para/>
/// The buffer the tiles fill is the size rounded up to whole tiles, and the header states that
/// rounded size as well as the real one. That is what accounts for the file: four thousand and
/// ninety-six plus the padded width times the padded height times four is the length of every one of
/// the eight samples, to the byte, and the padded pair is in each case the real pair rounded up to
/// the next multiple of 256. The stated resolution agrees too — the size divided by the pixels per
/// millimetre is the physical size the header states, in all eight.
/// <para/>
/// Three things about the layout are not visible in the arithmetic and were settled by measurement.
/// The tiles run left to right and then top to bottom: across every internal tile boundary the mean
/// difference is 0.44 to 1.56 times the difference between ordinary neighbouring pixels, whereas
/// reading them column-major makes those seams 4.5 to 47 times worse. The rows are stored bottom-up,
/// which only the vertical flip makes the word "eclipse" readable under. And the four bytes are a
/// big-endian word with channel n in bits 8n upward, so red is the last byte of the four; the first
/// is unused in the three-channel files, where it is zero in every pixel of every one of them.
/// <para/>
/// The four-channel files are CMYK, which was settled three ways over whole images rather than
/// samples: scoring all twenty-four assignments over the 577332 pixels the RGB and CMYK versions of
/// one picture share, the black plane being the only one that is ever large while the others are
/// small, and cyan running heavier than magenta and yellow across eleven thousand neutral mid-tones.
/// </remarks>
public readonly record struct EclipseTileFile
  : IImageFormatReader<EclipseTileFile>, IImageToRawImage<EclipseTileFile>,
    IImageFromRawImage<EclipseTileFile>, IImageFormatWriter<EclipseTileFile> {

  /// <summary>The two bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x07, 0x28];

  /// <summary>The name of the program that wrote every sample, which the header carries at 16.</summary>
  public const string Creator = "Eclipse";

  /// <summary>Where the header states the size, the colour space, the creator and the padded size.</summary>
  internal const int RevisionAt = 2, WidthAt = 4, HeightAt = 8, ColorSpaceAt = 12,
    CreatorAt = 16, CreatorVersionAt = 48, ChannelCountAt = 80,
    HorizontalResolutionAt = 84, VerticalResolutionAt = 92,
    PaddedWidthAt = 116, PaddedHeightAt = 120;

  /// <summary>How long the creator and its version are, both null-padded.</summary>
  internal const int CreatorLength = 32;

  /// <summary>The header, after which the tiles begin.</summary>
  public const int HeaderSize = 4096;

  /// <summary>How wide and tall a tile is, in pixels.</summary>
  public const int TileSize = 256;

  /// <summary>How many bytes a pixel takes, whatever the number of channels.</summary>
  public const int BytesPerPixel = 4;

  /// <summary>What the header states for a picture of three channels, and for one of four.</summary>
  internal const int RgbColorSpace = 0, CmykColorSpace = 1;
  internal const int RgbChannelCount = 3, CmykChannelCount = 4;

  static string IImageFormatMetadata<EclipseTileFile>.PrimaryExtension => ".tile";
  static string[] IImageFormatMetadata<EclipseTileFile>.FileExtensions => [".tile"];
  static EclipseTileFile IImageFormatReader<EclipseTileFile>.FromSpan(ReadOnlySpan<byte> data) => EclipseTileReader.FromSpan(data);
  static byte[] IImageFormatWriter<EclipseTileFile>.ToBytes(EclipseTileFile file) => EclipseTileWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<EclipseTileFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Two bytes of magic and the creator's name together, which nothing else carries.</summary>
  static bool? IImageFormatMetadata<EclipseTileFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= CreatorAt + CreatorLength
       && header[..Magic.Length].SequenceEqual(Magic)
       && header.Slice(CreatorAt, Creator.Length).SequenceEqual(Encoding.ASCII.GetBytes(Creator))
      ? true
      : null;

  /// <summary>Pixels across, as the header states.</summary>
  public int Width { get; init; }

  /// <summary>Pixels down, as the header states.</summary>
  public int Height { get; init; }

  /// <summary>How many channels a pixel has: three for RGB, four for CMYK.</summary>
  public int ChannelCount { get; init; }

  /// <summary>Which revision of the layout the header states; the samples show 0 and 1.</summary>
  public int Revision { get; init; }

  /// <summary>What wrote it, and which version of it did.</summary>
  public string? CreatorVersion { get; init; }

  /// <summary>Pixels a millimetre across and down, as the header states.</summary>
  public double HorizontalResolution { get; init; }

  /// <summary>Pixels a millimetre down, as the header states.</summary>
  public double VerticalResolution { get; init; }

  /// <summary>The picture, packed three bytes to a pixel, already the right way up.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(EclipseTileFile file)
    => new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData ?? new byte[file.Width * file.Height * 3],
    };

  public static EclipseTileFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      ChannelCount = RgbChannelCount,
      Revision = 0,
      CreatorVersion = "1.0",
      HorizontalResolution = 11.811023622047244,
      VerticalResolution = 11.811023622047244,
      PixelData = image.ToRgb24(),
    };
  }

  /// <summary>Rounds a size up to whole tiles, which is the buffer the tiles actually fill.</summary>
  internal static int Padded(int value) => (value + TileSize - 1) / TileSize * TileSize;
}
