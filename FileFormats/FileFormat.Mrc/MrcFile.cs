using System;
using FileFormat.Core;

namespace FileFormat.Mrc;

/// <summary>In-memory representation of an MRC2014 electron microscopy image.</summary>
public readonly record struct MrcFile : IImageFormatReader<MrcFile>, IImageToRawImage<MrcFile>, IImageFromRawImage<MrcFile>, IImageFormatWriter<MrcFile> {

  static string IImageFormatMetadata<MrcFile>.PrimaryExtension => ".mrc";
  static string[] IImageFormatMetadata<MrcFile>.FileExtensions => [".mrc", ".map"];
  static MrcFile IImageFormatReader<MrcFile>.FromSpan(ReadOnlySpan<byte> data) => MrcReader.FromSpan(data);
  static byte[] IImageFormatWriter<MrcFile>.ToBytes(MrcFile file) => MrcWriter.ToBytes(file);

  /// <summary>The fixed size of the MRC2014 header in bytes.</summary>
  public const int HeaderSize = 1024;

  /// <summary>The MAP magic bytes "MAP " at offset 208.</summary>
  internal static readonly byte[] MapMagic = [(byte)'M', (byte)'A', (byte)'P', (byte)' '];

  /// <summary>Machine stamp value indicating little-endian byte order.</summary>
  public const byte MachineStampLE = 0x44;

  /// <summary>Number of columns (NX).</summary>
  public int Width { get; init; }

  /// <summary>Number of rows (NY).</summary>
  public int Height { get; init; }

  /// <summary>Number of sections (NZ). For 2D images this is 1.</summary>
  public int Sections { get; init; }

  /// <summary>Data mode. 0=int8, 1=int16, 2=float32, 6=uint16.</summary>
  public int Mode { get; init; }

  /// <summary>Whether the file data is stored in big-endian byte order.</summary>
  public bool IsBigEndian { get; init; }

  /// <summary>Extended header size in bytes (NSYMBT at offset 92).</summary>
  public int ExtendedHeaderSize { get; init; }

  /// <summary>Extended header bytes (NSYMBT bytes after the 1024-byte header).</summary>
  public byte[] ExtendedHeader { get; init; }

  /// <summary>Raw pixel data in the file's native byte order.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MrcFile file) {
    if (file.Sections != 1)
      throw new NotSupportedException($"Only single-section (NZ=1) images are supported, got NZ={file.Sections}.");

    switch (file.Mode) {
      case 0: {
        // int8 -> Gray8
        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Gray8,
          PixelData = file.PixelData[..],
        };
      }
      case 1: {
        // int16 -> normalize signed int16 [-32768..32767] to uint16 [0..65535] -> Gray16 BE
        var pixelCount = file.Width * file.Height;
        var gray16 = new byte[pixelCount * 2];

        for (var i = 0; i < pixelCount; ++i) {
          var srcOffset = i * 2;
          short value;
          if (file.IsBigEndian)
            value = (short)(file.PixelData[srcOffset] << 8 | file.PixelData[srcOffset + 1]);
          else
            value = (short)(file.PixelData[srcOffset] | file.PixelData[srcOffset + 1] << 8);

          var unsigned = (ushort)(value + 32768);
          gray16[i * 2] = (byte)(unsigned >> 8);
          gray16[i * 2 + 1] = (byte)(unsigned & 0xFF);
        }

        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Gray16,
          PixelData = gray16,
        };
      }
      case 2: {
        // float32 -> normalize to [0..65535] -> Gray16 BE
        var pixelCount = file.Width * file.Height;
        var gray16 = new byte[pixelCount * 2];

        // First pass: find min and max
        var min = float.MaxValue;
        var max = float.MinValue;
        for (var i = 0; i < pixelCount; ++i) {
          var srcOffset = i * 4;
          float value;
          if (file.IsBigEndian) {
            var bits = file.PixelData[srcOffset] << 24
                       | file.PixelData[srcOffset + 1] << 16
                       | file.PixelData[srcOffset + 2] << 8
                       | file.PixelData[srcOffset + 3];
            value = BitConverter.Int32BitsToSingle(bits);
          } else
            value = BitConverter.ToSingle(file.PixelData, srcOffset);

          if (value < min)
            min = value;
          if (value > max)
            max = value;
        }

        var range = max - min;
        if (range <= 0f)
          range = 1f;

        // Second pass: normalize and write BE Gray16
        for (var i = 0; i < pixelCount; ++i) {
          var srcOffset = i * 4;
          float value;
          if (file.IsBigEndian) {
            var bits = file.PixelData[srcOffset] << 24
                       | file.PixelData[srcOffset + 1] << 16
                       | file.PixelData[srcOffset + 2] << 8
                       | file.PixelData[srcOffset + 3];
            value = BitConverter.Int32BitsToSingle(bits);
          } else
            value = BitConverter.ToSingle(file.PixelData, srcOffset);

          var normalized = (ushort)Math.Clamp((value - min) / range * 65535f + 0.5f, 0f, 65535f);
          gray16[i * 2] = (byte)(normalized >> 8);
          gray16[i * 2 + 1] = (byte)(normalized & 0xFF);
        }

        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Gray16,
          PixelData = gray16,
        };
      }
      case 6: {
        // uint16 -> Gray16 BE
        var pixelCount = file.Width * file.Height;
        var gray16 = new byte[pixelCount * 2];

        for (var i = 0; i < pixelCount; ++i) {
          var srcOffset = i * 2;
          byte hi, lo;
          if (file.IsBigEndian) {
            hi = file.PixelData[srcOffset];
            lo = file.PixelData[srcOffset + 1];
          } else {
            lo = file.PixelData[srcOffset];
            hi = file.PixelData[srcOffset + 1];
          }

          gray16[i * 2] = hi;
          gray16[i * 2 + 1] = lo;
        }

        return new() {
          Width = file.Width,
          Height = file.Height,
          Format = PixelFormat.Gray16,
          PixelData = gray16,
        };
      }
      default:
        throw new NotSupportedException($"Unsupported MRC mode: {file.Mode}.");
    }
  }

  public static MrcFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Sections = 1,
          Mode = 0,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Gray16: {
        // Gray16 BE -> uint16 LE (mode 6)
        var pixelCount = image.Width * image.Height;
        var pixelData = new byte[pixelCount * 2];

        for (var i = 0; i < pixelCount; ++i) {
          var srcOffset = i * 2;
          // BE (hi, lo) -> LE (lo, hi)
          pixelData[i * 2] = image.PixelData[srcOffset + 1];
          pixelData[i * 2 + 1] = image.PixelData[srcOffset];
        }

        return new() {
          Width = image.Width,
          Height = image.Height,
          Sections = 1,
          Mode = 6,
          PixelData = pixelData,
        };
      }
      default:
        throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Gray16} but got {image.Format}.", nameof(image));
    }
  }
}
