using System;
using FileFormat.Core;

namespace FileFormat.InterleafImage;

/// <summary>In-memory representation of an Interleaf image (.iimg).</summary>
/// <remarks>
/// Thirty-one bytes of header opening with <c>0x89 O P S</c>, then the pixels uncompressed. The
/// header states 75 by 75 for the resolution, then the size as two big-endian words and the depth as
/// a third: the sample says 800 by 600 at 24 bits, and 31 + 800 x 600 x 3 is 1440031, which is the
/// length of the file to the byte.
/// <para/>
/// The planes are interleaved by line rather than by pixel: a row of red, then that same row's
/// green, then its blue, and only then the next row. Read as ordinary packed RGB the picture comes
/// out as three squashed copies of itself side by side with colour fringing on every edge, which is
/// what said the rows were being cut in the wrong place; read a line at a time per plane it is a
/// single clean picture. Only one sample exists, so the reader states the arithmetic it depends on
/// and refuses anything the header does not account for exactly.
/// </remarks>
[FormatMagicBytes([0x89, 0x4F, 0x50, 0x53])]
public readonly record struct InterleafImageFile
  : IImageFormatReader<InterleafImageFile>, IImageToRawImage<InterleafImageFile>,
    IImageFromRawImage<InterleafImageFile>, IImageFormatWriter<InterleafImageFile> {

  /// <summary>The four bytes every one of these opens with.</summary>
  public static ReadOnlySpan<byte> Magic => [0x89, (byte)'O', (byte)'P', (byte)'S'];

  /// <summary>How long the header is, which is what the one sample's pixels account for exactly.</summary>
  public const int HeaderSize = 31;

  /// <summary>Where the header states the size and the depth, as big-endian words.</summary>
  internal const int WidthAt = 18, HeightAt = 20, BitsPerPixelAt = 22;

  /// <summary>Where the header states the resolution, as two big-endian words.</summary>
  internal const int HorizontalResolutionAt = 6, VerticalResolutionAt = 8;

  /// <summary>The three colour planes these carry, one line of each at a time.</summary>
  internal const int PlaneCount = 3;

  /// <summary>The depth this reads, being the only one the sample shows and the only one three planes fit.</summary>
  internal const int SupportedBitsPerPixel = 24;

  static string IImageFormatMetadata<InterleafImageFile>.PrimaryExtension => ".iimg";
  static string[] IImageFormatMetadata<InterleafImageFile>.FileExtensions => [".iimg"];
  static InterleafImageFile IImageFormatReader<InterleafImageFile>.FromSpan(ReadOnlySpan<byte> data) => InterleafImageReader.FromSpan(data);
  static byte[] IImageFormatWriter<InterleafImageFile>.ToBytes(InterleafImageFile file) => InterleafImageWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<InterleafImageFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Pixels across, as the header states.</summary>
  public int Width { get; init; }

  /// <summary>Pixels down, as the header states.</summary>
  public int Height { get; init; }

  /// <summary>Dots an inch across and down, as the header states.</summary>
  public int HorizontalResolution { get; init; }

  /// <summary>Dots an inch down, as the header states.</summary>
  public int VerticalResolution { get; init; }

  /// <summary>The picture, packed three bytes to a pixel.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(InterleafImageFile file)
    => new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData ?? new byte[file.Width * file.Height * PlaneCount],
    };

  public static InterleafImageFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    return new() {
      Width = image.Width,
      Height = image.Height,
      HorizontalResolution = 75,
      VerticalResolution = 75,
      PixelData = image.ToRgb24(),
    };
  }
}
