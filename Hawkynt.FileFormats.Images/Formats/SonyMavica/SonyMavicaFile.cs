using System;
using FileFormat.Core;

namespace FileFormat.SonyMavica;

/// <summary>In-memory representation of a Sony Mavica .411 image.</summary>
public readonly record struct SonyMavicaFile : IImageFormatReader<SonyMavicaFile>, IImageToRawImage<SonyMavicaFile>, IImageFromRawImage<SonyMavicaFile>, IImageFormatWriter<SonyMavicaFile> {

  static string IImageFormatMetadata<SonyMavicaFile>.PrimaryExtension => ".411";
  static string[] IImageFormatMetadata<SonyMavicaFile>.FileExtensions => [".411"];
  static SonyMavicaFile IImageFormatReader<SonyMavicaFile>.FromSpan(ReadOnlySpan<byte> data) => SonyMavicaReader.FromSpan(data);
  static byte[] IImageFormatWriter<SonyMavicaFile>.ToBytes(SonyMavicaFile file) => SonyMavicaWriter.ToBytes(file);

  /// <summary>Magic bytes: "MV" (0x4D 0x56).</summary>
  internal static readonly byte[] Magic = [0x4D, 0x56];

  /// <summary>Header size in bytes.</summary>
  internal const int HeaderSize = 8;

  /// <summary>Minimum valid file size.</summary>
  public const int MinFileSize = HeaderSize;

  /// <summary>The largest either dimension goes, which is what the header's words hold.</summary>
  public const int MaxDimension = 65535;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Image format code.</summary>
  public ushort Format { get; init; }

  /// <summary>Raw RGB pixel data (3 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this Mavica image to a platform-independent <see cref="RawImage"/> in Rgb24 format.</summary>
  public static RawImage ToRawImage(SonyMavicaFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  /// <summary>Creates a Mavica image from a platform-independent <see cref="RawImage"/>.</summary>
  /// <remarks>
  /// The header carries the dimensions, so any size fits; the pixels follow as three bytes each,
  /// which is what the reader hands straight out.
  /// </remarks>
  public static SonyMavicaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // The header states the size as words; a bigger picture would be written with its dimensions
    // wrapped and read back as a different one rather than as a broken one.
    if (image.Width is < 1 or > MaxDimension || image.Height is < 1 or > MaxDimension)
      throw new ArgumentException(
        $"A Mavica image is at most {MaxDimension}x{MaxDimension}; got {image.Width}x{image.Height}.", nameof(image));

    var source = image.EnsureFormat(PixelFormat.Rgb24);

    return new() {
      Width = source.Width,
      Height = source.Height,
      Format = 0,
      PixelData = source.PixelData[..(source.Width * source.Height * 3)],
    };
  }

}
