using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Fits;

/// <summary>In-memory representation of a FITS image.</summary>
[FormatMagicBytes([0x53, 0x49, 0x4D, 0x50, 0x4C, 0x45])]
public sealed class FitsFile : IImageFileFormat<FitsFile> {

  static string IImageFileFormat<FitsFile>.PrimaryExtension => ".fits";
  static string[] IImageFileFormat<FitsFile>.FileExtensions => [".fits", ".fit", ".fts"];
  static FitsFile IImageFileFormat<FitsFile>.FromFile(FileInfo file) => FitsReader.FromFile(file);
  static FitsFile IImageFileFormat<FitsFile>.FromBytes(byte[] data) => FitsReader.FromBytes(data);
  static FitsFile IImageFileFormat<FitsFile>.FromStream(Stream stream) => FitsReader.FromStream(stream);
  static RawImage IImageFileFormat<FitsFile>.ToRawImage(FitsFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<FitsFile>.ToBytes(FitsFile file) => FitsWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public FitsBitpix Bitpix { get; init; }
  public IReadOnlyList<FitsKeyword> Keywords { get; init; } = [];
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts this FITS image to a <see cref="RawImage"/>, preserving 16-bit precision where possible.</summary>
  public RawImage ToRawImage() {
    var width = this.Width;
    var height = this.Height;
    var src = this.PixelData;
    var pixelCount = width * height;

    switch (this.Bitpix) {
      case FitsBitpix.UInt8: {
        var result = new byte[pixelCount];
        Buffer.BlockCopy(src, 0, result, 0, Math.Min(src.Length, pixelCount));
        var palette = _BuildGrayscalePalette();
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Indexed8,
          PixelData = result,
          Palette = palette,
          PaletteCount = 256,
        };
      }
      case FitsBitpix.Int16: {
        var result = _Int16ToGray16BigEndian(src, pixelCount);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray16,
          PixelData = result,
        };
      }
      case FitsBitpix.Int32: {
        var result = _NormalizeInt32ToGray16BigEndian(src, pixelCount);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray16,
          PixelData = result,
        };
      }
      case FitsBitpix.Float32: {
        var result = _NormalizeFloat32ToGray16BigEndian(src, pixelCount);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray16,
          PixelData = result,
        };
      }
      case FitsBitpix.Float64: {
        var result = _NormalizeFloat64ToGray16BigEndian(src, pixelCount);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray16,
          PixelData = result,
        };
      }
      default:
        throw new NotSupportedException($"FITS BITPIX {(int)this.Bitpix} is not supported.");
    }
  }

  /// <summary>Creates a FITS grayscale image from a <see cref="RawImage"/>. 16-bit inputs (Gray16, Rgb48, Rgba64) produce BITPIX=16; all others produce BITPIX=8.</summary>
  public static FitsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Format is PixelFormat.Gray16 or PixelFormat.Rgb48 or PixelFormat.Rgba64) {
      // Preserve 16-bit precision: convert to Gray16 if needed, then encode as BITPIX=16
      var gray16 = image.Format == PixelFormat.Gray16 ? image : PixelConverter.Convert(image, PixelFormat.Gray16);
      var width = gray16.Width;
      var height = gray16.Height;
      var src = gray16.PixelData;
      var pixelCount = width * height;
      var result = new byte[pixelCount * 2];

      for (var i = 0; i < pixelCount; ++i) {
        var si = i * 2;
        if (si + 1 >= src.Length)
          break;

        // Gray16 is big-endian uint16 [hi, lo]; FITS Int16 is big-endian signed, so offset by -32768
        var unsigned = (ushort)(src[si] << 8 | src[si + 1]);
        var signed = (short)(unsigned - 32768);
        result[si] = (byte)(signed >> 8);
        result[si + 1] = (byte)(signed & 0xFF);
      }

      return new() {
        Width = width,
        Height = height,
        Bitpix = FitsBitpix.Int16,
        PixelData = result,
      };
    }

    {
      var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
      var width = bgra.Width;
      var height = bgra.Height;
      var src = bgra.PixelData;
      var pixelCount = width * height;
      var gray = new byte[pixelCount];

      for (var i = 0; i < pixelCount; ++i) {
        var si = i * 4;
        gray[i] = (byte)((src[si + 2] * 77 + src[si + 1] * 150 + src[si] * 29) >> 8);
      }

      return new() {
        Width = width,
        Height = height,
        Bitpix = FitsBitpix.UInt8,
        PixelData = gray,
      };
    }
  }

  /// <summary>Converts big-endian signed Int16 to Gray16 (big-endian uint16) by offsetting by 32768.</summary>
  private static byte[] _Int16ToGray16BigEndian(byte[] src, int count) {
    var dst = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      var offset = i * 2;
      if (offset + 1 >= src.Length)
        break;

      var signed = (short)(src[offset] << 8 | src[offset + 1]);
      var unsigned = (ushort)(signed + 32768);
      dst[offset] = (byte)(unsigned >> 8);
      dst[offset + 1] = (byte)(unsigned & 0xFF);
    }

    return dst;
  }

  /// <summary>Normalizes big-endian Int32 values to Gray16 (big-endian uint16).</summary>
  private static byte[] _NormalizeInt32ToGray16BigEndian(byte[] src, int count) {
    var min = int.MaxValue;
    var max = int.MinValue;
    for (var i = 0; i < count; ++i) {
      var offset = i * 4;
      if (offset + 3 >= src.Length)
        break;

      var val = src[offset] << 24 | src[offset + 1] << 16 | src[offset + 2] << 8 | src[offset + 3];
      if (val < min) min = val;
      if (val > max) max = val;
    }

    var range = (long)max - min;
    var dst = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      var offset = i * 4;
      if (offset + 3 >= src.Length)
        break;

      var val = src[offset] << 24 | src[offset + 1] << 16 | src[offset + 2] << 8 | src[offset + 3];
      var u16 = range == 0 ? (ushort)0 : (ushort)(((long)val - min) * 65535 / range);
      var di = i * 2;
      dst[di] = (byte)(u16 >> 8);
      dst[di + 1] = (byte)(u16 & 0xFF);
    }

    return dst;
  }

  /// <summary>Normalizes big-endian Float32 values to Gray16 (big-endian uint16).</summary>
  private static byte[] _NormalizeFloat32ToGray16BigEndian(byte[] src, int count) {
    var min = float.MaxValue;
    var max = float.MinValue;
    for (var i = 0; i < count; ++i) {
      var offset = i * 4;
      if (offset + 3 >= src.Length)
        break;

      var val = _ReadFloat32BE(src, offset);
      if (float.IsNaN(val) || float.IsInfinity(val))
        continue;

      if (val < min) min = val;
      if (val > max) max = val;
    }

    var range = max - min;
    var dst = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      var offset = i * 4;
      if (offset + 3 >= src.Length)
        break;

      var val = _ReadFloat32BE(src, offset);
      ushort u16;
      if (float.IsNaN(val) || float.IsInfinity(val))
        u16 = 0;
      else
        u16 = range == 0 ? (ushort)0 : (ushort)Math.Clamp((val - min) / range * 65535.0f, 0, 65535);

      var di = i * 2;
      dst[di] = (byte)(u16 >> 8);
      dst[di + 1] = (byte)(u16 & 0xFF);
    }

    return dst;
  }

  /// <summary>Normalizes big-endian Float64 values to Gray16 (big-endian uint16).</summary>
  private static byte[] _NormalizeFloat64ToGray16BigEndian(byte[] src, int count) {
    var min = double.MaxValue;
    var max = double.MinValue;
    for (var i = 0; i < count; ++i) {
      var offset = i * 8;
      if (offset + 7 >= src.Length)
        break;

      var val = _ReadFloat64BE(src, offset);
      if (double.IsNaN(val) || double.IsInfinity(val))
        continue;

      if (val < min) min = val;
      if (val > max) max = val;
    }

    var range = max - min;
    var dst = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      var offset = i * 8;
      if (offset + 7 >= src.Length)
        break;

      var val = _ReadFloat64BE(src, offset);
      ushort u16;
      if (double.IsNaN(val) || double.IsInfinity(val))
        u16 = 0;
      else
        u16 = range == 0 ? (ushort)0 : (ushort)Math.Clamp((val - min) / range * 65535.0, 0, 65535);

      var di = i * 2;
      dst[di] = (byte)(u16 >> 8);
      dst[di + 1] = (byte)(u16 & 0xFF);
    }

    return dst;
  }

  private static float _ReadFloat32BE(byte[] data, int offset) {
    Span<byte> buf = stackalloc byte[4];
    buf[0] = data[offset + 3];
    buf[1] = data[offset + 2];
    buf[2] = data[offset + 1];
    buf[3] = data[offset];
    return BitConverter.ToSingle(buf);
  }

  private static double _ReadFloat64BE(byte[] data, int offset) {
    Span<byte> buf = stackalloc byte[8];
    buf[0] = data[offset + 7];
    buf[1] = data[offset + 6];
    buf[2] = data[offset + 5];
    buf[3] = data[offset + 4];
    buf[4] = data[offset + 3];
    buf[5] = data[offset + 2];
    buf[6] = data[offset + 1];
    buf[7] = data[offset];
    return BitConverter.ToDouble(buf);
  }

  private static byte[] _BuildGrayscalePalette() {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      var po = i * 3;
      palette[po] = (byte)i;
      palette[po + 1] = (byte)i;
      palette[po + 2] = (byte)i;
    }

    return palette;
  }
}
