using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Fl32;

/// <summary>In-memory representation of a FL32 (FilmLight 32-bit float) image.</summary>
[FormatMagicBytes([0x46, 0x4C, 0x33, 0x32])]
public sealed class Fl32File : IImageFileFormat<Fl32File> {

  /// <summary>Magic bytes "FL32" as a uint32 little-endian value (842222662).</summary>
  public const uint Magic = 0x32334C46;

  /// <summary>Header size: 4 (magic) + 4 (height) + 4 (width) + 4 (channels) = 16 bytes.</summary>
  public const int HeaderSize = 16;

  static string IImageFileFormat<Fl32File>.PrimaryExtension => ".fl32";
  static string[] IImageFileFormat<Fl32File>.FileExtensions => [".fl32"];
  static Fl32File IImageFileFormat<Fl32File>.FromFile(FileInfo file) => Fl32Reader.FromFile(file);
  static Fl32File IImageFileFormat<Fl32File>.FromBytes(byte[] data) => Fl32Reader.FromBytes(data);
  static Fl32File IImageFileFormat<Fl32File>.FromStream(Stream stream) => Fl32Reader.FromStream(stream);
  static RawImage IImageFileFormat<Fl32File>.ToRawImage(Fl32File file) => file.ToRawImage();
  static byte[] IImageFileFormat<Fl32File>.ToBytes(Fl32File file) => Fl32Writer.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public int Channels { get; init; }

  /// <summary>Float samples in top-to-bottom, left-to-right, channel-interleaved order.</summary>
  public float[] PixelData { get; init; } = [];

  /// <summary>Converts this FL32 image to a 16-bit <see cref="RawImage"/>.</summary>
  public RawImage ToRawImage() {
    var width = this.Width;
    var height = this.Height;
    var channels = this.Channels;
    var src = this.PixelData;
    var pixelCount = width * height;

    switch (channels) {
      case 1: {
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var i = 0; i < pixelCount; ++i) {
          var v = src[i];
          if (float.IsNaN(v) || float.IsInfinity(v))
            continue;
          if (v < min) min = v;
          if (v > max) max = v;
        }

        var range = max - min;
        var scale = range > 0f ? 65535f / range : 0f;
        var result = new byte[pixelCount * 2];
        for (var i = 0; i < pixelCount; ++i) {
          var v = src[i];
          ushort u16;
          if (float.IsNaN(v) || float.IsInfinity(v))
            u16 = 0;
          else
            u16 = (ushort)Math.Clamp((v - min) * scale, 0, 65535);
          result[i * 2] = (byte)(u16 >> 8);
          result[i * 2 + 1] = (byte)u16;
        }

        return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = result };
      }
      case 3:
      case 4: {
        var sampleCount = pixelCount * channels;
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var i = 0; i < sampleCount; ++i) {
          var v = src[i];
          if (float.IsNaN(v) || float.IsInfinity(v))
            continue;
          if (v < min) min = v;
          if (v > max) max = v;
        }

        var range = max - min;
        var scale = range > 0f ? 65535f / range : 0f;

        if (channels == 3) {
          var result = new byte[pixelCount * 6];
          for (var i = 0; i < pixelCount; ++i) {
            var si = i * 3;
            var di = i * 6;
            for (var c = 0; c < 3; ++c) {
              var v = src[si + c];
              ushort u16;
              if (float.IsNaN(v) || float.IsInfinity(v))
                u16 = 0;
              else
                u16 = (ushort)Math.Clamp((v - min) * scale, 0, 65535);
              result[di + c * 2] = (byte)(u16 >> 8);
              result[di + c * 2 + 1] = (byte)u16;
            }
          }

          return new() { Width = width, Height = height, Format = PixelFormat.Rgb48, PixelData = result };
        }

        {
          var result = new byte[pixelCount * 8];
          for (var i = 0; i < pixelCount; ++i) {
            var si = i * 4;
            var di = i * 8;
            for (var c = 0; c < 4; ++c) {
              var v = src[si + c];
              ushort u16;
              if (float.IsNaN(v) || float.IsInfinity(v))
                u16 = 0;
              else
                u16 = (ushort)Math.Clamp((v - min) * scale, 0, 65535);
              result[di + c * 2] = (byte)(u16 >> 8);
              result[di + c * 2 + 1] = (byte)u16;
            }
          }

          return new() { Width = width, Height = height, Format = PixelFormat.Rgba64, PixelData = result };
        }
      }
      default:
        throw new NotSupportedException($"FL32 channel count {channels} is not supported.");
    }
  }

  /// <summary>Creates a <see cref="Fl32File"/> from a <see cref="RawImage"/>.</summary>
  public static Fl32File FromRawImage(RawImage image) {
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

        return new() { Width = width, Height = height, Channels = 1, PixelData = floats };
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

        return new() { Width = width, Height = height, Channels = 3, PixelData = floats };
      }
      case PixelFormat.Rgba64: {
        var src = image.PixelData;
        var pixelCount = width * height;
        var floats = new float[pixelCount * 4];
        for (var i = 0; i < pixelCount; ++i) {
          var si = i * 8;
          var di = i * 4;
          for (var c = 0; c < 4; ++c) {
            var u16 = (src[si + c * 2] << 8) | src[si + c * 2 + 1];
            floats[di + c] = u16 / 65535.0f;
          }
        }

        return new() { Width = width, Height = height, Channels = 4, PixelData = floats };
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
