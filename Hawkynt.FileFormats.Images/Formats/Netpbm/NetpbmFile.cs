using System;
using FileFormat.Core;

namespace FileFormat.Netpbm;

/// <summary>In-memory representation of a Netpbm image (PBM/PGM/PPM/PAM).</summary>
[FormatDetectionPriority(150)]
[FormatMimeType("image/x-portable-anymap", "image/x-portable-bitmap", "image/x-portable-graymap", "image/x-portable-pixmap", "image/x-portable-arbitrarymap")]
public readonly record struct NetpbmFile : IImageFormatReader<NetpbmFile>, IImageToRawImage<NetpbmFile>, IImageFromRawImage<NetpbmFile>, IImageFormatWriter<NetpbmFile> {

  static string IImageFormatMetadata<NetpbmFile>.PrimaryExtension => ".ppm";
  /// <summary>
  /// Every name Netpbm files are saved under, the plain ones and the explicit ones.
  /// </summary>
  /// <remarks>
  /// The <c>r</c> forms say the samples are raw rather than ASCII and <c>ppma</c> says the opposite,
  /// which the magic already states — P1 to P3 are the ASCII forms and P4 to P6 the raw ones — so
  /// the reader needs no telling. They were simply never claimed, and a file saved under one of them
  /// reached nothing at all.
  /// </remarks>
  static string[] IImageFormatMetadata<NetpbmFile>.FileExtensions => [".pbm", ".pgm", ".ppm", ".pnm", ".pam", ".ppma", ".rpbm", ".rpgm", ".rppm", ".rpnm"];
  static NetpbmFile IImageFormatReader<NetpbmFile>.FromSpan(ReadOnlySpan<byte> data) => NetpbmReader.FromSpan(data);
  static byte[] IImageFormatWriter<NetpbmFile>.ToBytes(NetpbmFile file) => NetpbmWriter.ToBytes(file);

  static bool? IImageFormatMetadata<NetpbmFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 2 || header[0] != 0x50 || header[1] < 0x31 || header[1] > 0x37)
      return null;
    if (header.Length >= 6 && header[1] == 0x37 && header[2] == 0x20 && header[3] == 0x33 && header[4] == 0x33 && header[5] == 0x32)
      return false;
    return true;
  }

  public NetpbmFormat Format { get; init; }
  public int Width { get; init; }
  public int Height { get; init; }
  public int MaxValue { get; init; }
  public int Channels { get; init; }
  public byte[] PixelData { get; init; }
  public string? TupleType { get; init; }

  /// <summary>
  /// Stretches samples stated against a maximum other than the full range of their width.
  /// </summary>
  /// <remarks>
  /// A Netpbm file names the largest value its samples take, and it is not obliged to be 255 or
  /// 65535 — one here states 1023, which is ten bits kept in sixteen. Handing those back as though
  /// they filled the sixteen makes the picture sixteen times too dark, which is what happened.
  /// </remarks>
  private static byte[] _StretchToFullRange(byte[] samples, int maxValue, bool sixteenBit) {
    if (maxValue <= 0)
      return samples;

    if (sixteenBit) {
      if (maxValue == ushort.MaxValue)
        return samples;

      var stretched = new byte[samples.Length];
      for (var at = 0; at + 1 < samples.Length; at += 2) {
        var value = (samples[at] << 8) | samples[at + 1];
        var full = Math.Min(ushort.MaxValue, value * ushort.MaxValue / maxValue);
        stretched[at] = (byte)(full >> 8);
        stretched[at + 1] = (byte)full;
      }

      return stretched;
    }

    if (maxValue == byte.MaxValue)
      return samples;

    var result = new byte[samples.Length];
    for (var i = 0; i < samples.Length; ++i)
      result[i] = (byte)Math.Min(byte.MaxValue, samples[i] * byte.MaxValue / maxValue);

    return result;
  }

  public static RawImage ToRawImage(NetpbmFile file) {
    var width = file.Width;
    var height = file.Height;
    var format = file.Format;
    var channels = file.Channels;
    var maxValue = file.MaxValue;
    var src = file.PixelData;

    switch (format) {
      case NetpbmFormat.PbmAscii:
      case NetpbmFormat.PbmBinary: {
        // Reader delivers 1 byte/pixel (0 or 1). PBM: 1=black, 0=white → invert for Gray8
        var pixelCount = width * height;
        var gray = new byte[pixelCount];
        for (var i = 0; i < pixelCount; ++i)
          gray[i] = src[i] == 0 ? (byte)255 : (byte)0;
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray8,
          PixelData = gray,
        };
      }
      case NetpbmFormat.PgmAscii:
      case NetpbmFormat.PgmBinary:
        if (maxValue <= 255)
          return new() {
            Width = width,
            Height = height,
            Format = PixelFormat.Gray8,
            PixelData = _StretchToFullRange(src[..], maxValue, false),
          };
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray16,
          PixelData = _StretchToFullRange(src[..], maxValue, true),
        };
      case NetpbmFormat.PpmAscii:
      case NetpbmFormat.PpmBinary:
        if (maxValue <= 255)
          return new() {
            Width = width,
            Height = height,
            Format = PixelFormat.Rgb24,
            PixelData = _StretchToFullRange(src[..], maxValue, false),
          };
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgb48,
          PixelData = _StretchToFullRange(src[..], maxValue, true),
        };
      case NetpbmFormat.Pam:
        switch (channels) {
          case 1:
            if (maxValue <= 255)
              return new() {
                Width = width,
                Height = height,
                Format = PixelFormat.Gray8,
                PixelData = _StretchToFullRange(src[..], maxValue, false),
              };
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Gray16,
              PixelData = _StretchToFullRange(src[..], maxValue, true),
            };
          case 2:
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.GrayAlpha16,
              PixelData = _StretchToFullRange(src[..], maxValue, false),
            };
          case 3:
            if (maxValue <= 255)
              return new() {
                Width = width,
                Height = height,
                Format = PixelFormat.Rgb24,
                PixelData = _StretchToFullRange(src[..], maxValue, false),
              };
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Rgb48,
              PixelData = _StretchToFullRange(src[..], maxValue, true),
            };
          case 4:
            if (maxValue <= 255)
              return new() {
                Width = width,
                Height = height,
                Format = PixelFormat.Rgba32,
                PixelData = _StretchToFullRange(src[..], maxValue, false),
              };
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Rgba64,
              PixelData = _StretchToFullRange(src[..], maxValue, true),
            };
          default:
            throw new NotSupportedException($"PAM with {channels} channels is not supported.");
        }
      default:
        throw new NotSupportedException($"Netpbm format {format} is not supported.");
    }
  }

  public static NetpbmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var width = image.Width;
    var height = image.Height;
    var src = image.PixelData;
    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Format = NetpbmFormat.PgmBinary,
          Width = width,
          Height = height,
          MaxValue = 255,
          Channels = 1,
          PixelData = src[..],
        };
      case PixelFormat.Gray16:
        return new() {
          Format = NetpbmFormat.PgmBinary,
          Width = width,
          Height = height,
          MaxValue = 65535,
          Channels = 1,
          PixelData = src[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Format = NetpbmFormat.PpmBinary,
          Width = width,
          Height = height,
          MaxValue = 255,
          Channels = 3,
          PixelData = src[..],
        };
      case PixelFormat.Rgb48:
        return new() {
          Format = NetpbmFormat.PpmBinary,
          Width = width,
          Height = height,
          MaxValue = 65535,
          Channels = 3,
          PixelData = src[..],
        };
      case PixelFormat.Rgba32:
        return new() {
          Format = NetpbmFormat.Pam,
          Width = width,
          Height = height,
          MaxValue = 255,
          Channels = 4,
          PixelData = src[..],
          TupleType = "RGB_ALPHA",
        };
      case PixelFormat.Rgba64:
        return new() {
          Format = NetpbmFormat.Pam,
          Width = width,
          Height = height,
          MaxValue = 65535,
          Channels = 4,
          PixelData = src[..],
          TupleType = "RGB_ALPHA",
        };
      case PixelFormat.GrayAlpha16:
        return new() {
          Format = NetpbmFormat.Pam,
          Width = width,
          Height = height,
          MaxValue = 255,
          Channels = 2,
          PixelData = src[..],
          TupleType = "GRAYSCALE_ALPHA",
        };
      default:
        throw new ArgumentException($"Pixel format {image.Format} is not supported by Netpbm.", nameof(image));
    }
  }
}
