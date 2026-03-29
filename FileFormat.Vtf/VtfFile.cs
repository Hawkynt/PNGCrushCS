using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Vtf;

/// <summary>In-memory representation of a VTF texture file.</summary>
[FormatMagicBytes([0x56, 0x54, 0x46, 0x00])]
public sealed class VtfFile : IImageFileFormat<VtfFile> {

  static string IImageFileFormat<VtfFile>.PrimaryExtension => ".vtf";
  static string[] IImageFileFormat<VtfFile>.FileExtensions => [".vtf"];
  static VtfFile IImageFileFormat<VtfFile>.FromFile(FileInfo file) => VtfReader.FromFile(file);
  static VtfFile IImageFileFormat<VtfFile>.FromBytes(byte[] data) => VtfReader.FromBytes(data);
  static VtfFile IImageFileFormat<VtfFile>.FromStream(Stream stream) => VtfReader.FromStream(stream);
  static byte[] IImageFileFormat<VtfFile>.ToBytes(VtfFile file) => VtfWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int MipmapCount { get; init; }
  public VtfFormat Format { get; init; }
  public VtfFlags Flags { get; init; }
  public int Frames { get; init; }
  public int VersionMajor { get; init; }
  public int VersionMinor { get; init; }
  public byte[]? ThumbnailData { get; init; }
  public IReadOnlyList<VtfSurface> Surfaces { get; init; } = [];

  public static RawImage ToRawImage(VtfFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Surfaces.Count == 0)
      throw new InvalidOperationException("VTF file contains no surfaces.");

    // Find the largest mip level (mip 0) for frame 0
    VtfSurface? best = null;
    foreach (var s in file.Surfaces)
      if (s.Frame == 0 && (best == null || s.MipLevel < best.MipLevel))
        best = s;
    best ??= file.Surfaces[0];

    var width = best.Width > 0 ? best.Width : file.Width;
    var height = best.Height > 0 ? best.Height : file.Height;
    var data = best.Data;

    return file.Format switch {
      VtfFormat.Dxt1 => _DecodeBc(data, width, height, Bc1Decoder.DecodeImage),
      VtfFormat.Dxt3 => _DecodeBc(data, width, height, Bc2Decoder.DecodeImage),
      VtfFormat.Dxt5 => _DecodeBc(data, width, height, Bc3Decoder.DecodeImage),
      VtfFormat.Rgba8888 => _CopyDirect(data, width, height, PixelFormat.Rgba32, 4),
      VtfFormat.Bgra8888 => _CopyDirect(data, width, height, PixelFormat.Bgra32, 4),
      VtfFormat.Argb8888 => _CopyDirect(data, width, height, PixelFormat.Argb32, 4),
      VtfFormat.Abgr8888 => _DecodeAbgr(data, width, height),
      VtfFormat.Rgb888 or VtfFormat.Rgb888Bluescreen => _CopyDirect(data, width, height, PixelFormat.Rgb24, 3),
      VtfFormat.Bgr888 or VtfFormat.Bgr888Bluescreen => _CopyDirect(data, width, height, PixelFormat.Bgr24, 3),
      VtfFormat.Rgb565 => _CopyDirect(data, width, height, PixelFormat.Rgb565, 2),
      VtfFormat.I8 => _CopyDirect(data, width, height, PixelFormat.Gray8, 1),
      VtfFormat.Ia88 => _CopyDirect(data, width, height, PixelFormat.GrayAlpha16, 2),
      VtfFormat.A8 => _DecodeAlpha8(data, width, height),
      VtfFormat.Uv88 => _DecodeUv88(data, width, height),
      _ => throw new NotSupportedException($"VTF format {file.Format} is not supported for conversion to RawImage.")
    };
  }

  public static VtfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    byte[] outputData;
    VtfFormat format;

    switch (image.Format) {
      case PixelFormat.Rgba32:
        outputData = image.PixelData;
        format = VtfFormat.Rgba8888;
        break;
      case PixelFormat.Bgra32:
        outputData = image.PixelData;
        format = VtfFormat.Bgra8888;
        break;
      case PixelFormat.Argb32:
        outputData = image.PixelData;
        format = VtfFormat.Argb8888;
        break;
      case PixelFormat.Rgb24:
        outputData = image.PixelData;
        format = VtfFormat.Rgb888;
        break;
      case PixelFormat.Bgr24:
        outputData = image.PixelData;
        format = VtfFormat.Bgr888;
        break;
      case PixelFormat.Gray8:
        outputData = image.PixelData;
        format = VtfFormat.I8;
        break;
      case PixelFormat.GrayAlpha16:
        outputData = image.PixelData;
        format = VtfFormat.Ia88;
        break;
      case PixelFormat.Rgb565:
        outputData = image.PixelData;
        format = VtfFormat.Rgb565;
        break;
      default:
        throw new NotSupportedException($"Cannot convert PixelFormat {image.Format} to VTF. Use Rgba32, Bgra32, Argb32, Rgb24, Bgr24, Gray8, GrayAlpha16, or Rgb565.");
    }

    return new VtfFile {
      Width = image.Width,
      Height = image.Height,
      MipmapCount = 1,
      Format = format,
      Flags = VtfFlags.None,
      Frames = 1,
      VersionMajor = 7,
      VersionMinor = 2,
      Surfaces = [new VtfSurface { Width = image.Width, Height = image.Height, MipLevel = 0, Frame = 0, Data = outputData }]
    };
  }

  private delegate void _BcDecoder(ReadOnlySpan<byte> data, int width, int height, Span<byte> output);

  private static RawImage _DecodeBc(byte[] data, int width, int height, _BcDecoder decoder) {
    var pixels = new byte[width * height * 4];
    decoder(data, width, height, pixels);
    return new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
  }

  private static RawImage _CopyDirect(byte[] data, int width, int height, PixelFormat format, int bytesPerPixel) {
    var expected = width * height * bytesPerPixel;
    var pixels = new byte[expected];
    data.AsSpan(0, Math.Min(data.Length, expected)).CopyTo(pixels);
    return new RawImage { Width = width, Height = height, Format = format, PixelData = pixels };
  }

  private static RawImage _DecodeAbgr(byte[] data, int width, int height) {
    var pixelCount = width * height;
    var pixels = new byte[pixelCount * 4];
    for (var i = 0; i < pixelCount; ++i) {
      var s = i * 4;
      if (s + 3 >= data.Length)
        break;
      pixels[s] = data[s + 3];     // R (from position 3 in ABGR)
      pixels[s + 1] = data[s + 2]; // G (from position 2 in ABGR)
      pixels[s + 2] = data[s + 1]; // B (from position 1 in ABGR)
      pixels[s + 3] = data[s];     // A (from position 0 in ABGR)
    }
    return new RawImage { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
  }

  private static RawImage _DecodeAlpha8(byte[] data, int width, int height) {
    // Alpha-only channel mapped to grayscale intensity
    var pixelCount = width * height;
    var pixels = new byte[pixelCount];
    data.AsSpan(0, Math.Min(data.Length, pixelCount)).CopyTo(pixels);
    return new RawImage { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
  }

  private static RawImage _DecodeUv88(byte[] data, int width, int height) {
    // Two-channel normal map; expose as GrayAlpha16 (U=gray, V=alpha)
    var expected = width * height * 2;
    var pixels = new byte[expected];
    data.AsSpan(0, Math.Min(data.Length, expected)).CopyTo(pixels);
    return new RawImage { Width = width, Height = height, Format = PixelFormat.GrayAlpha16, PixelData = pixels };
  }
}
