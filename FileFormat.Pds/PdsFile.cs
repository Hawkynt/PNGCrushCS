using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pds;

/// <summary>In-memory representation of a NASA Planetary Data System (PDS3) image.</summary>
[FormatMagicBytes([0x50, 0x44, 0x53, 0x5F])]
public sealed class PdsFile : IImageFileFormat<PdsFile> {

  static string IImageFileFormat<PdsFile>.PrimaryExtension => ".pds";
  static string[] IImageFileFormat<PdsFile>.FileExtensions => [".pds", ".lbl"];
  static PdsFile IImageFileFormat<PdsFile>.FromFile(FileInfo file) => PdsReader.FromFile(file);
  static PdsFile IImageFileFormat<PdsFile>.FromBytes(byte[] data) => PdsReader.FromBytes(data);
  static PdsFile IImageFileFormat<PdsFile>.FromStream(Stream stream) => PdsReader.FromStream(stream);
  static byte[] IImageFileFormat<PdsFile>.ToBytes(PdsFile file) => PdsWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Bits per sample (8 or 16).</summary>
  public int SampleBits { get; init; } = 8;

  /// <summary>Number of color bands (1 = grayscale, 3 = RGB).</summary>
  public int Bands { get; init; } = 1;

  /// <summary>Band storage organization.</summary>
  public PdsBandStorage BandStorage { get; init; }

  /// <summary>Sample data type.</summary>
  public PdsSampleType SampleType { get; init; }

  /// <summary>Raw pixel data bytes.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>All keyword=value labels from the PDS header.</summary>
  public Dictionary<string, string> Labels { get; init; } = new(StringComparer.OrdinalIgnoreCase);

  public static RawImage ToRawImage(PdsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.SampleBits == 8 && file.Bands == 1)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray8,
        PixelData = file.PixelData[..],
      };

    if (file.SampleBits == 8 && file.Bands == 3) {
      var pixelCount = file.Width * file.Height;
      byte[] result;

      switch (file.BandStorage) {
        case PdsBandStorage.SampleInterleaved:
          // already R,G,B,R,G,B,...
          result = file.PixelData[..];
          break;
        case PdsBandStorage.BandSequential:
          result = new byte[pixelCount * 3];
          for (var i = 0; i < pixelCount; ++i) {
            result[i * 3] = file.PixelData[i];
            result[i * 3 + 1] = file.PixelData[pixelCount + i];
            result[i * 3 + 2] = file.PixelData[pixelCount * 2 + i];
          }

          break;
        case PdsBandStorage.LineInterleaved:
          result = new byte[pixelCount * 3];
          var w = file.Width;
          for (var y = 0; y < file.Height; ++y) {
            var lineOffset = y * w * 3;
            for (var x = 0; x < w; ++x) {
              result[(y * w + x) * 3] = file.PixelData[lineOffset + x];
              result[(y * w + x) * 3 + 1] = file.PixelData[lineOffset + w + x];
              result[(y * w + x) * 3 + 2] = file.PixelData[lineOffset + w * 2 + x];
            }
          }

          break;
        default:
          throw new ArgumentException($"Unsupported band storage: {file.BandStorage}", nameof(file));
      }

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = result,
      };
    }

    if (file.SampleBits == 16 && file.Bands == 1) {
      var pixelCount = file.Width * file.Height;
      var result = new byte[pixelCount * 2];
      var isMsb = file.SampleType != PdsSampleType.LsbUnsigned16;
      for (var i = 0; i < pixelCount; ++i) {
        var byteOffset = i * 2;
        if (byteOffset + 1 >= file.PixelData.Length)
          break;

        var value = isMsb
          ? (ushort)((file.PixelData[byteOffset] << 8) | file.PixelData[byteOffset + 1])
          : (ushort)(file.PixelData[byteOffset] | (file.PixelData[byteOffset + 1] << 8));
        var di = i * 2;
        result[di] = (byte)(value >> 8);
        result[di + 1] = (byte)(value & 0xFF);
      }

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Gray16,
        PixelData = result,
      };
    }

    if (file.SampleBits == 16 && file.Bands == 3) {
      var pixelCount = file.Width * file.Height;
      var isMsb = file.SampleType != PdsSampleType.LsbUnsigned16;
      var result = new byte[pixelCount * 6];

      switch (file.BandStorage) {
        case PdsBandStorage.SampleInterleaved:
          for (var i = 0; i < pixelCount; ++i)
            for (var c = 0; c < 3; ++c) {
              var byteOffset = (i * 3 + c) * 2;
              if (byteOffset + 1 >= file.PixelData.Length)
                break;

              var value = isMsb
                ? (ushort)((file.PixelData[byteOffset] << 8) | file.PixelData[byteOffset + 1])
                : (ushort)(file.PixelData[byteOffset] | (file.PixelData[byteOffset + 1] << 8));
              var di = i * 6 + c * 2;
              result[di] = (byte)(value >> 8);
              result[di + 1] = (byte)(value & 0xFF);
            }

          break;
        case PdsBandStorage.BandSequential:
          for (var i = 0; i < pixelCount; ++i)
            for (var c = 0; c < 3; ++c) {
              var byteOffset = (c * pixelCount + i) * 2;
              if (byteOffset + 1 >= file.PixelData.Length)
                break;

              var value = isMsb
                ? (ushort)((file.PixelData[byteOffset] << 8) | file.PixelData[byteOffset + 1])
                : (ushort)(file.PixelData[byteOffset] | (file.PixelData[byteOffset + 1] << 8));
              var di = i * 6 + c * 2;
              result[di] = (byte)(value >> 8);
              result[di + 1] = (byte)(value & 0xFF);
            }

          break;
        case PdsBandStorage.LineInterleaved:
          var w = file.Width;
          for (var y = 0; y < file.Height; ++y)
            for (var x = 0; x < w; ++x)
              for (var c = 0; c < 3; ++c) {
                var byteOffset = (y * w * 3 + c * w + x) * 2;
                if (byteOffset + 1 >= file.PixelData.Length)
                  break;

                var value = isMsb
                  ? (ushort)((file.PixelData[byteOffset] << 8) | file.PixelData[byteOffset + 1])
                  : (ushort)(file.PixelData[byteOffset] | (file.PixelData[byteOffset + 1] << 8));
                var di = (y * w + x) * 6 + c * 2;
                result[di] = (byte)(value >> 8);
                result[di + 1] = (byte)(value & 0xFF);
              }

          break;
        default:
          throw new ArgumentException($"Unsupported band storage: {file.BandStorage}", nameof(file));
      }

      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb48,
        PixelData = result,
      };
    }

    throw new ArgumentException($"Unsupported PDS configuration: {file.SampleBits}-bit, {file.Bands} bands.", nameof(file));
  }

  public static PdsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          SampleBits = 8,
          Bands = 1,
          SampleType = PdsSampleType.UnsignedByte,
          BandStorage = PdsBandStorage.BandSequential,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          SampleBits = 8,
          Bands = 3,
          SampleType = PdsSampleType.UnsignedByte,
          BandStorage = PdsBandStorage.SampleInterleaved,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Gray16: {
        // Gray16 is big-endian uint16 [hi, lo]; PDS MSB unsigned 16 is also big-endian
        var pixelCount = image.Width * image.Height;
        var pixelData = new byte[pixelCount * 2];
        Buffer.BlockCopy(image.PixelData, 0, pixelData, 0, Math.Min(image.PixelData.Length, pixelData.Length));
        return new() {
          Width = image.Width,
          Height = image.Height,
          SampleBits = 16,
          Bands = 1,
          SampleType = PdsSampleType.MsbUnsigned16,
          BandStorage = PdsBandStorage.BandSequential,
          PixelData = pixelData,
        };
      }
      case PixelFormat.Rgb48: {
        // Rgb48 is big-endian 16-bit per channel [Rhi,Rlo,Ghi,Glo,Bhi,Blo]; sample-interleaved MSB
        var pixelCount = image.Width * image.Height;
        var pixelData = new byte[pixelCount * 6];
        Buffer.BlockCopy(image.PixelData, 0, pixelData, 0, Math.Min(image.PixelData.Length, pixelData.Length));
        return new() {
          Width = image.Width,
          Height = image.Height,
          SampleBits = 16,
          Bands = 3,
          SampleType = PdsSampleType.MsbUnsigned16,
          BandStorage = PdsBandStorage.SampleInterleaved,
          PixelData = pixelData,
        };
      }
      default:
        throw new ArgumentException($"Unsupported pixel format for PDS: {image.Format}", nameof(image));
    }
  }
}
