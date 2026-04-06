using System;
using FileFormat.Core;

namespace FileFormat.Ecw;

/// <summary>In-memory representation of an Enhanced Compressed Wavelet container image.</summary>
[FormatMagicBytes([0x65, 0x63, 0x77, 0x00])]
public readonly record struct EcwFile : IImageFormatReader<EcwFile>, IImageToRawImage<EcwFile>, IImageFromRawImage<EcwFile>, IImageFormatWriter<EcwFile> {

  internal const int HeaderSize = 16;

  /// <summary>ECW magic bytes: "ecw\0".</summary>
  private static ReadOnlySpan<byte> _Magic => [0x65, 0x63, 0x77, 0x00];

  static string IImageFormatMetadata<EcwFile>.PrimaryExtension => ".ecw";
  static string[] IImageFormatMetadata<EcwFile>.FileExtensions => [".ecw"];
  static EcwFile IImageFormatReader<EcwFile>.FromSpan(ReadOnlySpan<byte> data) => EcwReader.FromSpan(data);
  static byte[] IImageFormatWriter<EcwFile>.ToBytes(EcwFile file) => EcwWriter.ToBytes(file);

  static bool? IImageFormatMetadata<EcwFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 ? header[..4].SequenceEqual(_Magic) : null;

  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(EcwFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static EcwFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgb24)
      throw new ArgumentException("RawImage must use PixelFormat.Rgb24.", nameof(image));
    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
