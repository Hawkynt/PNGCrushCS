using System;
using FileFormat.Core;

namespace FileFormat.SonyPmp;

/// <summary>In-memory representation of a Sony Cyber-shot DSC-F1 picture (.pmp).</summary>
/// <remarks>The format is a documented 124-byte camera header followed by one complete JPEG.</remarks>
public readonly record struct SonyPmpFile : IImageFormatReader<SonyPmpFile>, IImageToRawImage<SonyPmpFile>, IImageFromRawImage<SonyPmpFile>, IImageFormatWriter<SonyPmpFile> {

  public const int HeaderSize = 124;
  public const int HeaderSizeOffset = 8;
  public const int JpegLengthOffset = 12;
  public static ReadOnlySpan<byte> JpegStart => [0xFF, 0xD8, 0xFF];

  static string IImageFormatMetadata<SonyPmpFile>.PrimaryExtension => ".pmp";
  static string[] IImageFormatMetadata<SonyPmpFile>.FileExtensions => [".pmp"];
  static SonyPmpFile IImageFormatReader<SonyPmpFile>.FromSpan(ReadOnlySpan<byte> data) => SonyPmpReader.FromSpan(data);
  static byte[] IImageFormatWriter<SonyPmpFile>.ToBytes(SonyPmpFile file) => SonyPmpWriter.ToBytes(file);

  static VideoMode[] IImageFormatMetadata<SonyPmpFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  static bool? IImageFormatMetadata<SonyPmpFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderSize + 3)
      return null;

    var stated = (header[HeaderSizeOffset] << 24) | (header[HeaderSizeOffset + 1] << 16)
                 | (header[HeaderSizeOffset + 2] << 8) | header[HeaderSizeOffset + 3];

    return stated == HeaderSize && header.Slice(HeaderSize, JpegStart.Length).SequenceEqual(JpegStart);
  }

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SonyPmpFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };

  /// <summary>Creates a PMP from any source image; JPEG encoding is performed by the shared JPEG codec.</summary>
  public static SonyPmpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    return new() { Width = rgb.Width, Height = rgb.Height, PixelData = rgb.PixelData[..] };
  }
}
