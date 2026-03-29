using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.WebP.Vp8;
using FileFormat.WebP.Vp8L;

namespace FileFormat.WebP;

/// <summary>In-memory representation of a WebP file with full VP8/VP8L pixel codec support.</summary>
public sealed class WebPFile : IImageFileFormat<WebPFile> {

  public required WebPFeatures Features { get; init; }
  public byte[] ImageData { get; init; } = [];
  public bool IsLossless { get; init; }
  public List<(string ChunkId, byte[] Data)> MetadataChunks { get; init; } = [];

  public static string PrimaryExtension => ".webp";
  public static string[] FileExtensions => [".webp", ".wep"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12
       && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
       && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50
       ? true
       : null;

  public static WebPFile FromFile(FileInfo file) => WebPReader.FromFile(file);
  public static WebPFile FromBytes(byte[] data) => WebPReader.FromBytes(data);
  public static WebPFile FromStream(Stream stream) => WebPReader.FromStream(stream);
  public static byte[] ToBytes(WebPFile file) => WebPWriter.ToBytes(file);

  public static RawImage ToRawImage(WebPFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var w = file.Features.Width;
    var h = file.Features.Height;

    if (file.IsLossless) {
      var rgba = Vp8LDecoder.Decode(file.ImageData, w, h, file.Features.HasAlpha);
      return new() {
        Width = w,
        Height = h,
        Format = file.Features.HasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
        PixelData = file.Features.HasAlpha ? rgba : _StripAlpha(rgba, w * h),
      };
    }

    var rgb = Vp8Decoder.Decode(file.ImageData, w, h);
    return new() {
      Width = w,
      Height = h,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  public static WebPFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var hasAlpha = image.Format is PixelFormat.Rgba32;
    var w = image.Width;
    var h = image.Height;

    // Encode as VP8L (lossless) for pixel-perfect round-trip
    var argb = _ToArgb(image);
    var vp8lData = Vp8LEncoder.Encode(argb, w, h, hasAlpha);

    return new() {
      Features = new WebPFeatures(w, h, hasAlpha, IsLossless: true, IsAnimated: false),
      ImageData = vp8lData,
      IsLossless = true,
    };
  }

  /// <summary>Convert RGBA byte array to ARGB uint array for VP8L encoder.</summary>
  private static uint[] _ToArgb(RawImage image) {
    var count = image.Width * image.Height;
    var argb = new uint[count];

    if (image.Format == PixelFormat.Rgba32) {
      for (var i = 0; i < count; ++i) {
        var off = i * 4;
        argb[i] = ((uint)image.PixelData[off + 3] << 24)
                   | ((uint)image.PixelData[off] << 16)
                   | ((uint)image.PixelData[off + 1] << 8)
                   | image.PixelData[off + 2];
      }
    } else if (image.Format == PixelFormat.Rgb24) {
      for (var i = 0; i < count; ++i) {
        var off = i * 3;
        argb[i] = 0xFF000000
                   | ((uint)image.PixelData[off] << 16)
                   | ((uint)image.PixelData[off + 1] << 8)
                   | image.PixelData[off + 2];
      }
    } else if (image.Format == PixelFormat.Gray8) {
      for (var i = 0; i < count; ++i) {
        var v = image.PixelData[i];
        argb[i] = 0xFF000000 | ((uint)v << 16) | ((uint)v << 8) | v;
      }
    } else {
      throw new ArgumentException($"Unsupported pixel format for WebP: {image.Format}.", nameof(image));
    }

    return argb;
  }

  /// <summary>Strip alpha channel from RGBA to produce RGB24 byte array.</summary>
  private static byte[] _StripAlpha(byte[] rgba, int pixelCount) {
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      rgb[i * 3] = rgba[i * 4];
      rgb[i * 3 + 1] = rgba[i * 4 + 1];
      rgb[i * 3 + 2] = rgba[i * 4 + 2];
    }
    return rgb;
  }
}
