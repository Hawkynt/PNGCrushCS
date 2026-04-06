using System;
using FileFormat.Core;

namespace FileFormat.Dpx;

/// <summary>In-memory representation of a DPX image.</summary>
[FormatMagicBytes([0x53, 0x44, 0x50, 0x58])]
[FormatMagicBytes([0x58, 0x50, 0x44, 0x53])]
public readonly record struct DpxFile : IImageFormatReader<DpxFile>, IImageToRawImage<DpxFile>, IImageFromRawImage<DpxFile>, IImageFormatWriter<DpxFile> {

  static string IImageFormatMetadata<DpxFile>.PrimaryExtension => ".dpx";
  static string[] IImageFormatMetadata<DpxFile>.FileExtensions => [".dpx"];
  static DpxFile IImageFormatReader<DpxFile>.FromSpan(ReadOnlySpan<byte> data) => DpxReader.FromSpan(data);
  static byte[] IImageFormatWriter<DpxFile>.ToBytes(DpxFile file) => DpxWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerElement { get; init; }
  public DpxDescriptor Descriptor { get; init; }
  public DpxPacking Packing { get; init; }
  public DpxTransfer Transfer { get; init; }
  public bool IsBigEndian { get; init; }
  public int ImageDataOffset { get; init; }
  public byte[] PixelData { get; init; }

  /// <summary>Converts this DPX image to a 16-bit <see cref="RawImage"/>. 8-bit sources remain 8-bit; 10/16-bit sources output Rgb48/Rgba64/Gray16.</summary>
  public static RawImage ToRawImage(DpxFile file) {
    var width = file.Width;
    var height = file.Height;
    var bits = file.BitsPerElement;
    var src = file.PixelData;
    var pixelCount = width * height;

    switch (file.Descriptor) {
      case DpxDescriptor.Rgb: {
        switch (bits) {
          case 8: {
            var result = new byte[pixelCount * 3];
            Buffer.BlockCopy(src, 0, result, 0, Math.Min(src.Length, result.Length));
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Rgb24,
              PixelData = result,
            };
          }
          case 10: {
            var result = new byte[pixelCount * 6];
            for (var i = 0; i < pixelCount; ++i) {
              var offset = i * 4;
              var word = _ReadUInt32(src, offset, file.IsBigEndian);
              var r = (ushort)(((word >> 22) & 0x3FF) * 65535 / 1023);
              var g = (ushort)(((word >> 12) & 0x3FF) * 65535 / 1023);
              var b = (ushort)(((word >> 2) & 0x3FF) * 65535 / 1023);
              var di = i * 6;
              result[di] = (byte)(r >> 8);
              result[di + 1] = (byte)r;
              result[di + 2] = (byte)(g >> 8);
              result[di + 3] = (byte)g;
              result[di + 4] = (byte)(b >> 8);
              result[di + 5] = (byte)b;
            }

            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Rgb48,
              PixelData = result,
            };
          }
          case 16: {
            var result = new byte[pixelCount * 6];
            for (var i = 0; i < pixelCount; ++i) {
              var offset = i * 6;
              var di = i * 6;
              var r = _ReadUInt16(src, offset, file.IsBigEndian);
              var g = _ReadUInt16(src, offset + 2, file.IsBigEndian);
              var b = _ReadUInt16(src, offset + 4, file.IsBigEndian);
              result[di] = (byte)(r >> 8);
              result[di + 1] = (byte)r;
              result[di + 2] = (byte)(g >> 8);
              result[di + 3] = (byte)g;
              result[di + 4] = (byte)(b >> 8);
              result[di + 5] = (byte)b;
            }

            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Rgb48,
              PixelData = result,
            };
          }
          default:
            throw new NotSupportedException($"DPX RGB with {bits} bits per element is not supported.");
        }
      }
      case DpxDescriptor.Rgba: {
        if (bits != 8)
          throw new NotSupportedException($"DPX RGBA with {bits} bits per element is not supported; only 8-bit is implemented.");

        var result = new byte[pixelCount * 4];
        Buffer.BlockCopy(src, 0, result, 0, Math.Min(src.Length, result.Length));
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgba32,
          PixelData = result,
        };
      }
      case DpxDescriptor.Luma: {
        switch (bits) {
          case 8: {
            var result = new byte[pixelCount];
            Buffer.BlockCopy(src, 0, result, 0, Math.Min(src.Length, result.Length));
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Gray8,
              PixelData = result,
            };
          }
          case 10: {
            var result = new byte[pixelCount * 2];
            for (var i = 0; i < pixelCount; ++i) {
              var offset = i * 4;
              var word = _ReadUInt32(src, offset, file.IsBigEndian);
              var v = (ushort)(((word >> 22) & 0x3FF) * 65535 / 1023);
              result[i * 2] = (byte)(v >> 8);
              result[i * 2 + 1] = (byte)v;
            }

            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Gray16,
              PixelData = result,
            };
          }
          case 16: {
            var result = new byte[pixelCount * 2];
            for (var i = 0; i < pixelCount; ++i) {
              var v = _ReadUInt16(src, i * 2, file.IsBigEndian);
              result[i * 2] = (byte)(v >> 8);
              result[i * 2 + 1] = (byte)v;
            }

            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Gray16,
              PixelData = result,
            };
          }
          default:
            throw new NotSupportedException($"DPX Luma with {bits} bits per element is not supported.");
        }
      }
      default:
        throw new NotSupportedException($"DPX descriptor {file.Descriptor} is not supported.");
    }
  }

  /// <summary>Creates a 16-bit RGB DPX image from a <see cref="RawImage"/>. Accepts Rgb48 natively or any convertible format.</summary>
  public static DpxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb48 = PixelConverter.Convert(image, PixelFormat.Rgb48);
    var width = rgb48.Width;
    var height = rgb48.Height;
    var src = rgb48.PixelData;
    var pixelCount = width * height;

    // Write as 16-bit big-endian RGB
    var pixelData = new byte[pixelCount * 6];
    for (var i = 0; i < pixelCount; ++i) {
      var si = i * 6;
      var di = i * 6;
      // Rgb48 is already big-endian: Rhi,Rlo,Ghi,Glo,Bhi,Blo
      pixelData[di] = src[si];
      pixelData[di + 1] = src[si + 1];
      pixelData[di + 2] = src[si + 2];
      pixelData[di + 3] = src[si + 3];
      pixelData[di + 4] = src[si + 4];
      pixelData[di + 5] = src[si + 5];
    }

    return new() {
      Width = width,
      Height = height,
      BitsPerElement = 16,
      Descriptor = DpxDescriptor.Rgb,
      Packing = DpxPacking.Packed,
      Transfer = DpxTransfer.Linear,
      IsBigEndian = true,
      ImageDataOffset = 0,
      PixelData = pixelData,
    };
  }

  private static uint _ReadUInt32(byte[] data, int offset, bool bigEndian) =>
    bigEndian
      ? (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3])
      : (uint)(data[offset + 3] << 24 | data[offset + 2] << 16 | data[offset + 1] << 8 | data[offset]);

  private static ushort _ReadUInt16(byte[] data, int offset, bool bigEndian) =>
    bigEndian
      ? (ushort)(data[offset] << 8 | data[offset + 1])
      : (ushort)(data[offset + 1] << 8 | data[offset]);
}
