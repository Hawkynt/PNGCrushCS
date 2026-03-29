using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Netpbm;

/// <summary>In-memory representation of a Netpbm image (PBM/PGM/PPM/PAM).</summary>
[FormatDetectionPriority(150)]
public sealed class NetpbmFile : IImageFileFormat<NetpbmFile> {

  static string IImageFileFormat<NetpbmFile>.PrimaryExtension => ".ppm";
  static string[] IImageFileFormat<NetpbmFile>.FileExtensions => [".pbm", ".pgm", ".ppm", ".pnm", ".pam"];
  static NetpbmFile IImageFileFormat<NetpbmFile>.FromFile(FileInfo file) => NetpbmReader.FromFile(file);
  static NetpbmFile IImageFileFormat<NetpbmFile>.FromBytes(byte[] data) => NetpbmReader.FromBytes(data);
  static NetpbmFile IImageFileFormat<NetpbmFile>.FromStream(Stream stream) => NetpbmReader.FromStream(stream);
  static RawImage IImageFileFormat<NetpbmFile>.ToRawImage(NetpbmFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<NetpbmFile>.ToBytes(NetpbmFile file) => NetpbmWriter.ToBytes(file);

  static bool? IImageFileFormat<NetpbmFile>.MatchesSignature(ReadOnlySpan<byte> header) {
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
  public byte[] PixelData { get; init; } = [];
  public string? TupleType { get; init; }

  public RawImage ToRawImage() {
    var width = this.Width;
    var height = this.Height;
    var format = this.Format;
    var channels = this.Channels;
    var maxValue = this.MaxValue;
    var src = this.PixelData;

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
            PixelData = src[..],
          };
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray16,
          PixelData = src[..],
        };
      case NetpbmFormat.PpmAscii:
      case NetpbmFormat.PpmBinary:
        if (maxValue <= 255)
          return new() {
            Width = width,
            Height = height,
            Format = PixelFormat.Rgb24,
            PixelData = src[..],
          };
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgb48,
          PixelData = src[..],
        };
      case NetpbmFormat.Pam:
        switch (channels) {
          case 1:
            if (maxValue <= 255)
              return new() {
                Width = width,
                Height = height,
                Format = PixelFormat.Gray8,
                PixelData = src[..],
              };
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Gray16,
              PixelData = src[..],
            };
          case 2:
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.GrayAlpha16,
              PixelData = src[..],
            };
          case 3:
            if (maxValue <= 255)
              return new() {
                Width = width,
                Height = height,
                Format = PixelFormat.Rgb24,
                PixelData = src[..],
              };
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Rgb48,
              PixelData = src[..],
            };
          case 4:
            if (maxValue <= 255)
              return new() {
                Width = width,
                Height = height,
                Format = PixelFormat.Rgba32,
                PixelData = src[..],
              };
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Rgba64,
              PixelData = src[..],
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
