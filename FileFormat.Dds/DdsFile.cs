using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Dds;

/// <summary>In-memory representation of a DDS (DirectDraw Surface) file.</summary>
[FormatMagicBytes([0x44, 0x44, 0x53, 0x20])]
public readonly record struct DdsFile : IImageFormatReader<DdsFile>, IImageToRawImage<DdsFile>, IImageFormatWriter<DdsFile> {

  static string IImageFormatMetadata<DdsFile>.PrimaryExtension => ".dds";
  static string[] IImageFormatMetadata<DdsFile>.FileExtensions => [".dds"];
  static DdsFile IImageFormatReader<DdsFile>.FromSpan(ReadOnlySpan<byte> data) => DdsReader.FromSpan(data);
  static byte[] IImageFormatWriter<DdsFile>.ToBytes(DdsFile file) => DdsWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Depth { get; init; }
  public int MipMapCount { get; init; }
  public DdsFormat Format { get; init; }
  public bool HasDx10Header { get; init; }
  public IReadOnlyList<DdsSurface> Surfaces { get; init; }

  public static RawImage ToRawImage(DdsFile file) {
    if (file.Surfaces.Count == 0)
      throw new InvalidOperationException("DDS file contains no surfaces.");

    var surface = file.Surfaces[0];
    var width = surface.Width > 0 ? surface.Width : file.Width;
    var height = surface.Height > 0 ? surface.Height : file.Height;
    var data = surface.Data;

    return file.Format switch {
      DdsFormat.Dxt1 => _DecodeBc(data, width, height, Bc1Decoder.DecodeImage),
      DdsFormat.Dxt3 => _DecodeBc(data, width, height, Bc2Decoder.DecodeImage),
      DdsFormat.Dxt5 => _DecodeBc(data, width, height, Bc3Decoder.DecodeImage),
      DdsFormat.Bc4 => _DecodeBc(data, width, height, Bc4Decoder.DecodeImage),
      DdsFormat.Bc5 => _DecodeBc(data, width, height, Bc5Decoder.DecodeImage),
      DdsFormat.Bc6HUnsigned => _DecodeBc(data, width, height, (d, w, h, o) => Bc6HDecoder.DecodeImage(d, w, h, o, false)),
      DdsFormat.Bc6HSigned => _DecodeBc(data, width, height, (d, w, h, o) => Bc6HDecoder.DecodeImage(d, w, h, o, true)),
      DdsFormat.Bc7 => _DecodeBc(data, width, height, Bc7Decoder.DecodeImage),
      DdsFormat.Rgb => _DecodeUncompressed(data, width, height, PixelFormat.Rgb24, 3),
      DdsFormat.Rgba => _DecodeUncompressed(data, width, height, PixelFormat.Rgba32, 4),
      _ => throw new NotSupportedException($"DDS format {file.Format} is not supported for conversion to RawImage.")
    };
  }

  public static DdsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    byte[] outputData;
    DdsFormat format;

    switch (image.Format) {
      case PixelFormat.Rgba32:
        outputData = image.PixelData;
        format = DdsFormat.Rgba;
        break;
      case PixelFormat.Bgra32:
        outputData = _SwapChannels4(image.PixelData, image.Width * image.Height, 2, 1, 0, 3);
        format = DdsFormat.Rgba;
        break;
      case PixelFormat.Argb32:
        outputData = _SwapChannels4(image.PixelData, image.Width * image.Height, 1, 2, 3, 0);
        format = DdsFormat.Rgba;
        break;
      case PixelFormat.Rgb24:
        outputData = image.PixelData;
        format = DdsFormat.Rgb;
        break;
      case PixelFormat.Bgr24:
        outputData = _SwapChannels3(image.PixelData, image.Width * image.Height);
        format = DdsFormat.Rgb;
        break;
      default:
        throw new NotSupportedException($"Cannot convert PixelFormat {image.Format} to DDS. Use Rgba32, Rgb24, Bgra32, Bgr24, or Argb32.");
    }

    return new DdsFile {
      Width = image.Width,
      Height = image.Height,
      Depth = 1,
      MipMapCount = 1,
      Format = format,
      Surfaces = [new DdsSurface { Width = image.Width, Height = image.Height, MipLevel = 0, Data = outputData }]
    };
  }

  private delegate void _BcDecoder(ReadOnlySpan<byte> data, int width, int height, Span<byte> output);

  private static RawImage _DecodeBc(byte[] data, int width, int height, _BcDecoder decoder) {
    var pixels = new byte[width * height * 4];
    decoder(data, width, height, pixels);
    return new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
  }

  private static RawImage _DecodeUncompressed(byte[] data, int width, int height, PixelFormat format, int bytesPerPixel) {
    var expected = width * height * bytesPerPixel;
    var pixels = new byte[expected];
    data.AsSpan(0, Math.Min(data.Length, expected)).CopyTo(pixels);
    return new RawImage { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  private static byte[] _SwapChannels4(byte[] src, int pixelCount, int rIndex, int gIndex, int bIndex, int aIndex) {
    var result = new byte[pixelCount * 4];
    for (var i = 0; i < pixelCount; ++i) {
      var s = i * 4;
      var d = i * 4;
      result[d] = src[s + rIndex];
      result[d + 1] = src[s + gIndex];
      result[d + 2] = src[s + bIndex];
      result[d + 3] = src[s + aIndex];
    }
    return result;
  }

  private static byte[] _SwapChannels3(byte[] src, int pixelCount) {
    var result = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var s = i * 3;
      result[s] = src[s + 2];
      result[s + 1] = src[s + 1];
      result[s + 2] = src[s];
    }
    return result;
  }
}
