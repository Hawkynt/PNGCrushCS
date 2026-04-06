using System;
using FileFormat.Core;

namespace FileFormat.Pfm;

/// <summary>In-memory representation of a PFM (Portable Float Map) image.</summary>
public readonly record struct PfmFile : IImageFormatReader<PfmFile>, IImageToRawImage<PfmFile>, IImageFromRawImage<PfmFile>, IImageFormatWriter<PfmFile> {

  static string IImageFormatMetadata<PfmFile>.PrimaryExtension => ".pfm";
  static string[] IImageFormatMetadata<PfmFile>.FileExtensions => [".pfm"];
  static PfmFile IImageFormatReader<PfmFile>.FromSpan(ReadOnlySpan<byte> data) => PfmReader.FromSpan(data);
  static byte[] IImageFormatWriter<PfmFile>.ToBytes(PfmFile file) => PfmWriter.ToBytes(file);

  static bool? IImageFormatMetadata<PfmFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 3 && header[0] == 0x50 && (header[1] == 0x46 || header[1] == 0x66)
      && (header[2] == 0x0A || header[2] == 0x0D || header[2] == 0x20)
      ? true : null;

  public int Width { get; init; }
  public int Height { get; init; }
  public PfmColorMode ColorMode { get; init; }

  /// <summary>Absolute scale factor from the PFM header.</summary>
  public float Scale { get; init; }

  /// <summary>Whether the pixel data was stored in little-endian byte order.</summary>
  public bool IsLittleEndian { get; init; }

  /// <summary>
  /// Float samples in top-to-bottom, left-to-right order.
  /// Grayscale: Width * Height floats. RGB: Width * Height * 3 floats (R, G, B interleaved).
  /// </summary>
  public float[] PixelData { get; init; }

  /// <summary>Converts this PFM image to a 16-bit <see cref="RawImage"/>. Grayscale PFM outputs Gray16, color PFM outputs Rgb48.</summary>
  public static RawImage ToRawImage(PfmFile file) {
    var width = file.Width;
    var height = file.Height;
    var src = file.PixelData;

    if (file.ColorMode == PfmColorMode.Grayscale) {
      var pixelCount = width * height;

      var min = float.MaxValue;
      var max = float.MinValue;
      for (var i = 0; i < pixelCount; ++i) {
        var v = src[i];
        if (v < min) min = v;
        if (v > max) max = v;
      }

      var range = max - min;
      var scale = range > 0f ? 65535f / range : 0f;

      var result = new byte[pixelCount * 2];
      for (var i = 0; i < pixelCount; ++i) {
        var u16 = (ushort)Math.Clamp((src[i] - min) * scale, 0, 65535);
        result[i * 2] = (byte)(u16 >> 8);
        result[i * 2 + 1] = (byte)u16;
      }

      return new() {
        Width = width,
        Height = height,
        Format = PixelFormat.Gray16,
        PixelData = result,
      };
    }

    {
      var pixelCount = width * height;
      var sampleCount = pixelCount * 3;

      var min = float.MaxValue;
      var max = float.MinValue;
      for (var i = 0; i < sampleCount; ++i) {
        var v = src[i];
        if (v < min) min = v;
        if (v > max) max = v;
      }

      var range = max - min;
      var scale = range > 0f ? 65535f / range : 0f;

      var result = new byte[pixelCount * 6];
      for (var i = 0; i < pixelCount; ++i) {
        var si = i * 3;
        var di = i * 6;
        for (var c = 0; c < 3; ++c) {
          var u16 = (ushort)Math.Clamp((src[si + c] - min) * scale, 0, 65535);
          result[di + c * 2] = (byte)(u16 >> 8);
          result[di + c * 2 + 1] = (byte)u16;
        }
      }

      return new() {
        Width = width,
        Height = height,
        Format = PixelFormat.Rgb48,
        PixelData = result,
      };
    }
  }

  /// <summary>Creates a <see cref="PfmFile"/> from a <see cref="RawImage"/>. Accepts Gray16/Rgb48 natively, or any format convertible via PixelConverter.</summary>
  public static PfmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var width = image.Width;
    var height = image.Height;

    switch (image.Format) {
      case PixelFormat.Gray16: {
        var src = image.PixelData;
        var pixelCount = width * height;
        var floats = new float[pixelCount];
        for (var i = 0; i < pixelCount; ++i) {
          var u16 = (src[i * 2] << 8) | src[i * 2 + 1];
          floats[i] = u16 / 65535.0f;
        }

        return new() {
          Width = width,
          Height = height,
          ColorMode = PfmColorMode.Grayscale,
          Scale = 1.0f,
          IsLittleEndian = true,
          PixelData = floats,
        };
      }
      case PixelFormat.Rgb48: {
        var src = image.PixelData;
        var pixelCount = width * height;
        var floats = new float[pixelCount * 3];
        for (var i = 0; i < pixelCount; ++i) {
          var si = i * 6;
          var di = i * 3;
          for (var c = 0; c < 3; ++c) {
            var u16 = (src[si + c * 2] << 8) | src[si + c * 2 + 1];
            floats[di + c] = u16 / 65535.0f;
          }
        }

        return new() {
          Width = width,
          Height = height,
          ColorMode = PfmColorMode.Rgb,
          Scale = 1.0f,
          IsLittleEndian = true,
          PixelData = floats,
        };
      }
      case PixelFormat.Indexed8:
      case PixelFormat.Gray8: {
        var gray16 = PixelConverter.Convert(image, PixelFormat.Gray16);
        return FromRawImage(gray16);
      }
      default: {
        var rgb48 = PixelConverter.Convert(image, PixelFormat.Rgb48);
        return FromRawImage(rgb48);
      }
    }
  }
}
