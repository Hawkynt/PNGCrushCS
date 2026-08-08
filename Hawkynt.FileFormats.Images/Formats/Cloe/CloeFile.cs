using System;
using FileFormat.Core;

namespace FileFormat.Cloe;

/// <summary>In-memory representation of a Cloe Ray-Tracer image image.</summary>
public readonly record struct CloeFile : IImageFormatReader<CloeFile>, IImageToRawImage<CloeFile>, IImageFromRawImage<CloeFile>, IImageFormatWriter<CloeFile> {

  internal const int HeaderSize = 8;

  /// <summary>The largest side a Cloe picture may state, past which the header is not one.</summary>
  internal const int MaxDimension = 65535;

  static string IImageFormatMetadata<CloeFile>.PrimaryExtension => ".clo";

  /// <summary>Both names the ray-tracer's own pictures carry.</summary>
  /// <remarks>
  /// <c>.clo</c> and <c>.cloe</c> are one format under a short name and a long one. <c>.clo</c> is
  /// shared with unrelated data files, so what identifies a picture is the header alone: two
  /// little-endian lengths that between them have to account for the pixels present.
  /// </remarks>
  static string[] IImageFormatMetadata<CloeFile>.FileExtensions => [".clo", ".cloe"];
  static CloeFile IImageFormatReader<CloeFile>.FromSpan(ReadOnlySpan<byte> data) => CloeReader.FromSpan(data);
  static byte[] IImageFormatWriter<CloeFile>.ToBytes(CloeFile file) => CloeWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(CloeFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static CloeFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
