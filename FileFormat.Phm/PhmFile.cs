using System;
using FileFormat.Core;

namespace FileFormat.Phm;

/// <summary>In-memory representation of a PHM (Portable Half Map) image — half-precision float variant of PFM.</summary>
public readonly record struct PhmFile : IImageFormatReader<PhmFile>, IImageToRawImage<PhmFile>, IImageFromRawImage<PhmFile>, IImageFormatWriter<PhmFile> {

  static string IImageFormatMetadata<PhmFile>.PrimaryExtension => ".phm";
  static string[] IImageFormatMetadata<PhmFile>.FileExtensions => [".phm"];
  static PhmFile IImageFormatReader<PhmFile>.FromSpan(ReadOnlySpan<byte> data) => PhmReader.FromSpan(data);
  static byte[] IImageFormatWriter<PhmFile>.ToBytes(PhmFile file) => PhmWriter.ToBytes(file);

  static bool? IImageFormatMetadata<PhmFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 3 && header[0] == 0x50 && (header[1] == 0x48 || header[1] == 0x68)
      && (header[2] == 0x0A || header[2] == 0x0D || header[2] == 0x20)
      ? true : null;

  public int Width { get; init; }
  public int Height { get; init; }
  public PhmColorMode ColorMode { get; init; }
  public float Scale { get; init; }
  public bool IsLittleEndian { get; init; }

  /// <summary>
  /// Half-precision samples in top-to-bottom, left-to-right order.
  /// Grayscale: Width * Height halves. RGB: Width * Height * 3 halves (R, G, B interleaved).
  /// </summary>
  public Half[] PixelData { get; init; }

  /// <summary>Converts this PHM image to a 16-bit <see cref="RawImage"/>. Grayscale outputs Gray16, color outputs Rgb48.</summary>
  public static RawImage ToRawImage(PhmFile file) {
    var width = file.Width;
    var height = file.Height;
    var src = file.PixelData;

    if (file.ColorMode == PhmColorMode.Grayscale) {
      var pixelCount = width * height;

      var min = (float)Half.MaxValue;
      var max = (float)Half.MinValue;
      for (var i = 0; i < pixelCount; ++i) {
        var v = (float)src[i];
        if (Half.IsNaN(src[i]) || Half.IsInfinity(src[i]))
          continue;
        if (v < min) min = v;
        if (v > max) max = v;
      }

      var range = max - min;
      var scale = range > 0f ? 65535f / range : 0f;

      var result = new byte[pixelCount * 2];
      for (var i = 0; i < pixelCount; ++i) {
        ushort u16;
        if (Half.IsNaN(src[i]) || Half.IsInfinity(src[i]))
          u16 = 0;
        else
          u16 = (ushort)Math.Clamp(((float)src[i] - min) * scale, 0, 65535);
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

      var min = (float)Half.MaxValue;
      var max = (float)Half.MinValue;
      for (var i = 0; i < sampleCount; ++i) {
        var v = (float)src[i];
        if (Half.IsNaN(src[i]) || Half.IsInfinity(src[i]))
          continue;
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
          ushort u16;
          if (Half.IsNaN(src[si + c]) || Half.IsInfinity(src[si + c]))
            u16 = 0;
          else
            u16 = (ushort)Math.Clamp(((float)src[si + c] - min) * scale, 0, 65535);
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

  /// <summary>Creates a <see cref="PhmFile"/> from a <see cref="RawImage"/>.</summary>
  public static PhmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var width = image.Width;
    var height = image.Height;

    switch (image.Format) {
      case PixelFormat.Gray16: {
        var src = image.PixelData;
        var pixelCount = width * height;
        var halves = new Half[pixelCount];
        for (var i = 0; i < pixelCount; ++i) {
          var u16 = (src[i * 2] << 8) | src[i * 2 + 1];
          halves[i] = (Half)(u16 / 65535.0f);
        }

        return new() {
          Width = width,
          Height = height,
          ColorMode = PhmColorMode.Grayscale,
          Scale = 1.0f,
          IsLittleEndian = true,
          PixelData = halves,
        };
      }
      case PixelFormat.Rgb48: {
        var src = image.PixelData;
        var pixelCount = width * height;
        var halves = new Half[pixelCount * 3];
        for (var i = 0; i < pixelCount; ++i) {
          var si = i * 6;
          var di = i * 3;
          for (var c = 0; c < 3; ++c) {
            var u16 = (src[si + c * 2] << 8) | src[si + c * 2 + 1];
            halves[di + c] = (Half)(u16 / 65535.0f);
          }
        }

        return new() {
          Width = width,
          Height = height,
          ColorMode = PhmColorMode.Rgb,
          Scale = 1.0f,
          IsLittleEndian = true,
          PixelData = halves,
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
