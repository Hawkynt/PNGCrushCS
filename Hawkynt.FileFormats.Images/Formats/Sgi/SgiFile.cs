using System;
using FileFormat.Core;

namespace FileFormat.Sgi;

/// <summary>In-memory representation of an SGI image.</summary>
[FormatMagicBytes([0x01, 0xDA])]
[FormatMimeType("image/x-sgi", "image/sgi")]
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
          PixelData = _FlipRows(file.PixelData, width, height, 1),
        };
      case 1 when bpc == 2:
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray16,
          PixelData = _FlipRows(file.PixelData, width, height, 2),
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
          PixelData = _FlipRows(src, width, height, 1),
        };
      case PixelFormat.Gray16:
        return new() {
          Width = width,
          Height = height,
          Channels = 1,
          BytesPerChannel = 2,
          Compression = SgiCompression.None,
          ColorMode = SgiColorMode.Normal,
          PixelData = _FlipRows(src, width, height, 2),
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

  /// <summary>
  /// Interleaves the channel planes, turning the file's bottom-up rows the right way up.
  /// </summary>
  /// <remarks>
  /// SGI stores its first scanline at the bottom of the picture, as OpenGL does. Copying row y to row
  /// y therefore returned every image upside down — which nothing that compares an image's size can
  /// see, since a flipped picture has exactly the dimensions of the one it flips.
  /// </remarks>
  private static byte[] _Deplanarize(byte[] planar, int width, int height, int channels) {
    var planeSize = width * height;
    var result = new byte[planeSize * channels];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var source = (y * width) + x;
        var target = (((height - 1 - y) * width) + x);
        for (var c = 0; c < channels; ++c)
          result[(target * channels) + c] = planar[(c * planeSize) + source];
      }
    return result;
  }

  /// <inheritdoc cref="_Deplanarize"/>
  private static byte[] _Deplanarize16(byte[] planar, int width, int height, int channels) {
    var planeSize = width * height * 2;
    var pixelCount = width * height;
    var result = new byte[pixelCount * channels * 2];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var pixelIndex = (y * width) + x;
        var targetIndex = ((height - 1 - y) * width) + x;
        for (var c = 0; c < channels; ++c) {
          var srcOffset = (c * planeSize) + (pixelIndex * 2);
          var dstOffset = ((targetIndex * channels) + c) * 2;
          result[dstOffset]     = planar[srcOffset];
          result[dstOffset + 1] = planar[srcOffset + 1];
        }
      }
    return result;
  }

  /// <summary>Splits packed pixels into channel planes, writing the bottom row first.</summary>
  /// <remarks>The mirror of <see cref="_Deplanarize"/>, so what is written reads back the same way up.</remarks>
  private static byte[] _Planarize(byte[] interleaved, int width, int height, int channels) {
    var planeSize = width * height;
    var result = new byte[planeSize * channels];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var source = (y * width) + x;
        var target = ((height - 1 - y) * width) + x;
        for (var c = 0; c < channels; ++c)
          result[(c * planeSize) + target] = interleaved[(source * channels) + c];
      }
    return result;
  }

  private static byte[] _Planarize16(byte[] interleaved, int width, int height, int channels) {
    var planeSize = width * height * 2;
    var pixelCount = width * height;
    var result = new byte[pixelCount * channels * 2];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var pixelIndex = (y * width) + x;
        var targetIndex = ((height - 1 - y) * width) + x;
        for (var c = 0; c < channels; ++c) {
          var srcOffset = ((pixelIndex * channels) + c) * 2;
          var dstOffset = (c * planeSize) + (targetIndex * 2);
          result[dstOffset]     = interleaved[srcOffset];
          result[dstOffset + 1] = interleaved[srcOffset + 1];
        }
      }
    return result;
  }

  /// <summary>Turns a single-plane image the right way up.</summary>
  /// <remarks>SGI's first scanline is the bottom one, as OpenGL's is.</remarks>
  private static byte[] _FlipRows(byte[] pixels, int width, int height, int bytesPerPixel) {
    var stride = width * bytesPerPixel;
    var result = new byte[stride * height];
    for (var y = 0; y < height; ++y) {
      var source = y * stride;
      if (source + stride > pixels.Length)
        break;

      pixels.AsSpan(source, stride).CopyTo(result.AsSpan((height - 1 - y) * stride));
    }

    return result;
  }
}
