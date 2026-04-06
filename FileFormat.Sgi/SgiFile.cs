using System;
using FileFormat.Core;

namespace FileFormat.Sgi;

/// <summary>In-memory representation of an SGI image.</summary>
[FormatMagicBytes([0x01, 0xDA])]
public readonly record struct SgiFile : IImageFormatReader<SgiFile>, IImageToRawImage<SgiFile>, IImageFromRawImage<SgiFile>, IImageFormatWriter<SgiFile> {

  static string IImageFormatMetadata<SgiFile>.PrimaryExtension => ".sgi";
  static string[] IImageFormatMetadata<SgiFile>.FileExtensions => [".sgi", ".rgb", ".bw", ".iris", ".rgba", ".inta"];
  static SgiFile IImageFormatReader<SgiFile>.FromSpan(ReadOnlySpan<byte> data) => SgiReader.FromSpan(data);
  static byte[] IImageFormatWriter<SgiFile>.ToBytes(SgiFile file) => SgiWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Channels { get; init; }
  public int BytesPerChannel { get; init; }
  public SgiCompression Compression { get; init; }
  public SgiColorMode ColorMode { get; init; }
  public string ImageName { get; init; }
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SgiFile file) {
    var width = file.Width;
    var height = file.Height;
    var channels = file.Channels;
    var bpc = file.BytesPerChannel;
    switch (channels) {
      case 1 when bpc == 1:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray8,
          PixelData = file.PixelData[..],
        };
      case 1 when bpc == 2:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray16,
          PixelData = file.PixelData[..],
        };
      case 3 when bpc == 1:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgb24,
          PixelData = _Deplanarize(file.PixelData, width, height, 3),
        };
      case 3 when bpc == 2:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgb48,
          PixelData = _Deplanarize16(file.PixelData, width, height, 3),
        };
      case 4 when bpc == 1:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgba32,
          PixelData = _Deplanarize(file.PixelData, width, height, 4),
        };
      case 4 when bpc == 2:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgba64,
          PixelData = _Deplanarize16(file.PixelData, width, height, 4),
        };
      default:
        throw new NotSupportedException($"SGI image with {channels} channels and {bpc} bytes/channel is not supported.");
    }
  }

  public static SgiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var width = image.Width;
    var height = image.Height;
    var src = image.PixelData;
    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = width,
          Height = height,
          Channels = 1,
          BytesPerChannel = 1,
          Compression = SgiCompression.None,
          ColorMode = SgiColorMode.Normal,
          PixelData = src[..],
        };
      case PixelFormat.Gray16:
        return new() {
          Width = width,
          Height = height,
          Channels = 1,
          BytesPerChannel = 2,
          Compression = SgiCompression.None,
          ColorMode = SgiColorMode.Normal,
          PixelData = src[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Width = width,
          Height = height,
          Channels = 3,
          BytesPerChannel = 1,
          Compression = SgiCompression.None,
          ColorMode = SgiColorMode.Normal,
          PixelData = _Planarize(src, width, height, 3),
        };
      case PixelFormat.Rgb48:
        return new() {
          Width = width,
          Height = height,
          Channels = 3,
          BytesPerChannel = 2,
          Compression = SgiCompression.None,
          ColorMode = SgiColorMode.Normal,
          PixelData = _Planarize16(src, width, height, 3),
        };
      case PixelFormat.Rgba32:
        return new() {
          Width = width,
          Height = height,
          Channels = 4,
          BytesPerChannel = 1,
          Compression = SgiCompression.None,
          ColorMode = SgiColorMode.Normal,
          PixelData = _Planarize(src, width, height, 4),
        };
      case PixelFormat.Rgba64:
        return new() {
          Width = width,
          Height = height,
          Channels = 4,
          BytesPerChannel = 2,
          Compression = SgiCompression.None,
          ColorMode = SgiColorMode.Normal,
          PixelData = _Planarize16(src, width, height, 4),
        };
      default:
        throw new ArgumentException($"Pixel format {image.Format} is not supported by SGI.", nameof(image));
    }
  }

  private static byte[] _Deplanarize(byte[] planar, int width, int height, int channels) {
    var planeSize = width * height;
    var result = new byte[planeSize * channels];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var pixelIndex = y * width + x;
        for (var c = 0; c < channels; ++c)
          result[pixelIndex * channels + c] = planar[c * planeSize + pixelIndex];
      }
    return result;
  }

  private static byte[] _Deplanarize16(byte[] planar, int width, int height, int channels) {
    var planeSize = width * height * 2;
    var pixelCount = width * height;
    var result = new byte[pixelCount * channels * 2];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var pixelIndex = y * width + x;
        for (var c = 0; c < channels; ++c) {
          var srcOffset = c * planeSize + pixelIndex * 2;
          var dstOffset = (pixelIndex * channels + c) * 2;
          result[dstOffset]     = planar[srcOffset];
          result[dstOffset + 1] = planar[srcOffset + 1];
        }
      }
    return result;
  }

  private static byte[] _Planarize(byte[] interleaved, int width, int height, int channels) {
    var planeSize = width * height;
    var result = new byte[planeSize * channels];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var pixelIndex = y * width + x;
        for (var c = 0; c < channels; ++c)
          result[c * planeSize + pixelIndex] = interleaved[pixelIndex * channels + c];
      }
    return result;
  }

  private static byte[] _Planarize16(byte[] interleaved, int width, int height, int channels) {
    var planeSize = width * height * 2;
    var pixelCount = width * height;
    var result = new byte[pixelCount * channels * 2];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var pixelIndex = y * width + x;
        for (var c = 0; c < channels; ++c) {
          var srcOffset = (pixelIndex * channels + c) * 2;
          var dstOffset = c * planeSize + pixelIndex * 2;
          result[dstOffset]     = interleaved[srcOffset];
          result[dstOffset + 1] = interleaved[srcOffset + 1];
        }
      }
    return result;
  }
}
