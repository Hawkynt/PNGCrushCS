using System;
using FileFormat.Core;

namespace FileFormat.Qoi;

/// <summary>In-memory representation of a QOI image.</summary>
[FormatMagicBytes([0x71, 0x6F, 0x69, 0x66])]
[FormatMimeType("image/qoi", "image/x-qoi")]
public readonly record struct QoiFile : IImageFormatReader<QoiFile>, IImageToRawImage<QoiFile>, IImageFromRawImage<QoiFile>, IImageFormatWriter<QoiFile> {

  static string IImageFormatMetadata<QoiFile>.PrimaryExtension => ".qoi";
  static string[] IImageFormatMetadata<QoiFile>.FileExtensions => [".qoi"];
  static QoiFile IImageFormatReader<QoiFile>.FromSpan(ReadOnlySpan<byte> data) => QoiReader.FromSpan(data);
  static byte[] IImageFormatWriter<QoiFile>.ToBytes(QoiFile file) => QoiWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public QoiChannels Channels { get; init; }
  public QoiColorSpace ColorSpace { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(QoiFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.Channels == QoiChannels.Rgb ? PixelFormat.Rgb24 : PixelFormat.Rgba32,
      PixelData = file.PixelData[..],
    };
  }

  public static QoiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgba32, PixelFormat.Rgb24);
    var channels = image.Format == PixelFormat.Rgb24 ? QoiChannels.Rgb : QoiChannels.Rgba;

    return new() {
      Width = image.Width,
      Height = image.Height,
      Channels = channels,
      PixelData = image.PixelData[..],
    };
  }
}
