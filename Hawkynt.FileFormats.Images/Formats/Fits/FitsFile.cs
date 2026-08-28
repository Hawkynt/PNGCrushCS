using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Fits;

/// <summary>In-memory representation of a FITS image.</summary>
[FormatMagicBytes([0x53, 0x49, 0x4D, 0x50, 0x4C, 0x45])]
public readonly record struct FitsFile : IImageFormatReader<FitsFile>, IImageToRawImage<FitsFile>, IImageFromRawImage<FitsFile>, IImageFormatWriter<FitsFile> {

  static string IImageFormatMetadata<FitsFile>.PrimaryExtension => ".fits";
  static string[] IImageFormatMetadata<FitsFile>.FileExtensions => [".fits", ".fit", ".fts"];
  static FitsFile IImageFormatReader<FitsFile>.FromSpan(ReadOnlySpan<byte> data) => FitsReader.FromSpan(data);
  static byte[] IImageFormatWriter<FitsFile>.ToBytes(FitsFile file) => FitsWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  /// <summary>Number of planes in a conventional NAXIS3 colour cube; one for a normal 2D image.</summary>
  public int Channels { get; init; }
  public FitsBitpix Bitpix { get; init; }
  public IReadOnlyList<FitsKeyword> Keywords { get; init; }
  public byte[] PixelData { get; init; }

  /// <summary>How many bytes one sample takes for a given data type.</summary>
  internal static int BytesPerSample(FitsBitpix bitpix) => Math.Abs((int)bitpix) / 8;

  /// <summary>Turns the rows the right way up.</summary>
  /// <remarks>
  /// FITS puts the origin at the bottom left, as the mathematics it was built for does, so the first
  /// row in the file is the bottom of the picture. Taking the rows in file order gives a picture
  /// that is upside down and otherwise perfectly plausible.
  /// </remarks>
  internal static byte[] FlipRows(byte[] pixels, int width, int height, int bytesPerSample) {
    var stride = width * bytesPerSample;
    var result = new byte[stride * height];
    for (var y = 0; y < height; ++y) {
      var from = (height - 1 - y) * stride;
      if (from + stride <= pixels.Length)
        Array.Copy(pixels, from, result, y * stride, stride);
    }

    return result;
  }

  private static byte[] _FlipPlaneRows(byte[] pixels, int width, int height, int bytesPerSample, int channels) {
    var planeSize = checked(width * height * bytesPerSample);
    var result = new byte[checked(planeSize * channels)];
    for (var channel = 0; channel < channels; ++channel) {
      var planeOffset = channel * planeSize;
      if (planeOffset >= pixels.Length)
        break;

      var available = Math.Min(planeSize, pixels.Length - planeOffset);
      var plane = new byte[available];
      Array.Copy(pixels, planeOffset, plane, 0, available);
      var flipped = FlipRows(plane, width, height, bytesPerSample);
      Array.Copy(flipped, 0, result, planeOffset, Math.Min(flipped.Length, result.Length - planeOffset));
    }

    return result;
  }

  /// <summary>Converts this FITS image to a <see cref="RawImage"/>, preserving 16-bit precision and conventional RGB(A) cubes.</summary>
  public static RawImage ToRawImage(FitsFile file) {
    var width = file.Width;
    var height = file.Height;
    var channels = file.Channels is 3 or 4 ? file.Channels : 1;
    var bytesPerSample = BytesPerSample(file.Bitpix);
    var src = channels == 1
      ? FlipRows(file.PixelData, width, height, bytesPerSample)
      : _FlipPlaneRows(file.PixelData, width, height, bytesPerSample, channels);
    var pixelCount = checked(width * height);

    if (channels > 1)
      return _DecodeColourCube(src, width, height, channels, file.Bitpix);

    switch (file.Bitpix) {
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
        throw new NotSupportedException($"FITS BITPIX {(int)file.Bitpix} is not supported.");
    }
  }

  private static RawImage _DecodeColourCube(byte[] src, int width, int height, int channels, FitsBitpix bitpix) {
    var pixelCount = checked(width * height);
    var bytesPerSample = BytesPerSample(bitpix);
    var planeBytes = checked(pixelCount * bytesPerSample);

    if (bitpix == FitsBitpix.UInt8) {
      var result = new byte[checked(pixelCount * channels)];
      for (var i = 0; i < pixelCount; ++i)
        for (var channel = 0; channel < channels; ++channel) {
          var source = channel * planeBytes + i;
          if (source < src.Length)
            result[i * channels + channel] = src[source];
        }

      return new() {
        Width = width,
        Height = height,
        Format = channels == 4 ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
        PixelData = result,
      };
    }

    var normalizedPlanes = new byte[channels][];
    for (var channel = 0; channel < channels; ++channel) {
      var offset = channel * planeBytes;
      var available = Math.Max(0, Math.Min(planeBytes, src.Length - offset));
      var plane = new byte[planeBytes];
      if (available > 0)
        Array.Copy(src, offset, plane, 0, available);

      normalizedPlanes[channel] = bitpix switch {
        FitsBitpix.Int16 => _Int16ToGray16BigEndian(plane, pixelCount),
        FitsBitpix.Int32 => _NormalizeInt32ToGray16BigEndian(plane, pixelCount),
        FitsBitpix.Float32 => _NormalizeFloat32ToGray16BigEndian(plane, pixelCount),
        FitsBitpix.Float64 => _NormalizeFloat64ToGray16BigEndian(plane, pixelCount),
        _ => throw new NotSupportedException($"FITS BITPIX {(int)bitpix} is not supported for colour cubes."),
      };
    }

    var deep = new byte[checked(pixelCount * channels * 2)];
    for (var i = 0; i < pixelCount; ++i)
      for (var channel = 0; channel < channels; ++channel) {
        var destination = (i * channels + channel) * 2;
        var source = i * 2;
        deep[destination] = normalizedPlanes[channel][source];
        deep[destination + 1] = normalizedPlanes[channel][source + 1];
      }

    return new() {
      Width = width,
      Height = height,
      Format = channels == 4 ? PixelFormat.Rgba64 : PixelFormat.Rgb48,
      PixelData = deep,
    };
  }

  /// <summary>Creates a FITS image from a <see cref="RawImage"/>, retaining greyscale/colour channels and 16-bit precision.</summary>
  public static FitsFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = image.Width;
    var height = image.Height;
    var pixelCount = checked(width * height);

    if (image.Format is PixelFormat.Gray16 or PixelFormat.Gray10) {
      var gray16 = image.EnsureFormat(PixelFormat.Gray16);
      return new() {
        Width = width,
        Height = height,
        Channels = 1,
        Bitpix = FitsBitpix.Int16,
        Keywords = _Unsigned16Keywords(),
        PixelData = FlipRows(_Unsigned16ToSignedBigEndian(gray16.PixelData, pixelCount), width, height, 2),
      };
    }

    if (image.Format is PixelFormat.Rgb48 or PixelFormat.Rgba64) {
      var channels = image.Format == PixelFormat.Rgba64 ? 4 : 3;
      var deep = image.EnsureFormat(channels == 4 ? PixelFormat.Rgba64 : PixelFormat.Rgb48);
      return new() {
        Width = width,
        Height = height,
        Channels = channels,
        Bitpix = FitsBitpix.Int16,
        Keywords = _Unsigned16Keywords(),
        PixelData = _InterleavedUnsigned16ToPlanarSigned(deep.PixelData, width, height, channels),
      };
    }

    if (image.Format is PixelFormat.Gray8 or PixelFormat.GrayAlpha16) {
      var gray = image.EnsureFormat(PixelFormat.Gray8);
      return new() {
        Width = width,
        Height = height,
        Channels = 1,
        Bitpix = FitsBitpix.UInt8,
        Keywords = Array.Empty<FitsKeyword>(),
        PixelData = FlipRows(gray.PixelData[..pixelCount], width, height, 1),
      };
    }

    var keepAlpha = image.Format is PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Argb32;
    var channels8 = keepAlpha ? 4 : 3;
    var colour = image.EnsureFormat(keepAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24);
    return new() {
      Width = width,
      Height = height,
      Channels = channels8,
      Bitpix = FitsBitpix.UInt8,
      Keywords = Array.Empty<FitsKeyword>(),
      PixelData = _Interleaved8ToPlanar(colour.PixelData, width, height, channels8),
    };
  }

  private static IReadOnlyList<FitsKeyword> _Unsigned16Keywords()
    => [new FitsKeyword("BZERO", "32768", "offset restoring unsigned 16-bit samples")];

  private static byte[] _Unsigned16ToSignedBigEndian(byte[] src, int pixelCount) {
    var result = new byte[pixelCount * 2];
    for (var i = 0; i < pixelCount; ++i) {
      var si = i * 2;
      if (si + 1 >= src.Length)
        break;

      var unsigned = (ushort)(src[si] << 8 | src[si + 1]);
      var signed = unchecked((short)(unsigned - 32768));
      result[si] = (byte)(signed >> 8);
      result[si + 1] = (byte)signed;
    }

    return result;
  }

  private static byte[] _Interleaved8ToPlanar(byte[] src, int width, int height, int channels) {
    var pixelCount = checked(width * height);
    var planeSize = pixelCount;
    var planar = new byte[checked(planeSize * channels)];
    for (var y = 0; y < height; ++y) {
      var sourceY = height - 1 - y;
      for (var x = 0; x < width; ++x) {
        var sourcePixel = (sourceY * width + x) * channels;
        var planePixel = y * width + x;
        for (var channel = 0; channel < channels; ++channel) {
          var source = sourcePixel + channel;
          if (source < src.Length)
            planar[channel * planeSize + planePixel] = src[source];
        }
      }
    }

    return planar;
  }

  private static byte[] _InterleavedUnsigned16ToPlanarSigned(byte[] src, int width, int height, int channels) {
    var pixelCount = checked(width * height);
    var planeSize = checked(pixelCount * 2);
    var planar = new byte[checked(planeSize * channels)];
    for (var y = 0; y < height; ++y) {
      var sourceY = height - 1 - y;
      for (var x = 0; x < width; ++x) {
        var sourcePixel = (sourceY * width + x) * channels * 2;
        var planePixel = (y * width + x) * 2;
        for (var channel = 0; channel < channels; ++channel) {
          var source = sourcePixel + channel * 2;
          if (source + 1 >= src.Length)
            continue;

          var unsigned = (ushort)(src[source] << 8 | src[source + 1]);
          var signed = unchecked((short)(unsigned - 32768));
          var destination = channel * planeSize + planePixel;
          planar[destination] = (byte)(signed >> 8);
          planar[destination + 1] = (byte)signed;
        }
      }
    }

    return planar;
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
