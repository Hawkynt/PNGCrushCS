using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Qoi;

/// <summary>In-memory representation of a QOI image.</summary>
[FormatMagicBytes([0x71, 0x6F, 0x69, 0x66])]
public sealed class QoiFile : IImageFileFormat<QoiFile> {

  static string IImageFileFormat<QoiFile>.PrimaryExtension => ".qoi";
  static string[] IImageFileFormat<QoiFile>.FileExtensions => [".qoi"];
  static QoiFile IImageFileFormat<QoiFile>.FromFile(FileInfo file) => QoiReader.FromFile(file);
  static QoiFile IImageFileFormat<QoiFile>.FromBytes(byte[] data) => QoiReader.FromBytes(data);
  static QoiFile IImageFileFormat<QoiFile>.FromStream(Stream stream) => QoiReader.FromStream(stream);
  static byte[] IImageFileFormat<QoiFile>.ToBytes(QoiFile file) => QoiWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public QoiChannels Channels { get; init; }
  public QoiColorSpace ColorSpace { get; init; }
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(QoiFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.Channels == QoiChannels.Rgb ? PixelFormat.Rgb24 : PixelFormat.Rgba32,
      PixelData = file.PixelData[..],
    };
  }

  public static QoiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    QoiChannels channels;
    if (image.Format == PixelFormat.Rgb24)
      channels = QoiChannels.Rgb;
    else if (image.Format == PixelFormat.Rgba32)
      channels = QoiChannels.Rgba;
    else
      throw new ArgumentException($"Expected {PixelFormat.Rgb24} or {PixelFormat.Rgba32} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      Channels = channels,
      PixelData = image.PixelData[..],
    };
  }
}
