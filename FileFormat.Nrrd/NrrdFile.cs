using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Nrrd;

/// <summary>In-memory representation of a NRRD (Nearly Raw Raster Data) file.</summary>
[FormatMagicBytes([0x4E, 0x52, 0x52, 0x44])]
public sealed class NrrdFile : IImageFileFormat<NrrdFile> {

  static string IImageFileFormat<NrrdFile>.PrimaryExtension => ".nrrd";
  static string[] IImageFileFormat<NrrdFile>.FileExtensions => [".nrrd", ".nhdr"];
  static NrrdFile IImageFileFormat<NrrdFile>.FromFile(FileInfo file) => NrrdReader.FromFile(file);
  static NrrdFile IImageFileFormat<NrrdFile>.FromBytes(byte[] data) => NrrdReader.FromBytes(data);
  static NrrdFile IImageFileFormat<NrrdFile>.FromStream(Stream stream) => NrrdReader.FromStream(stream);
  static RawImage IImageFileFormat<NrrdFile>.ToRawImage(NrrdFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<NrrdFile>.ToBytes(NrrdFile file) => NrrdWriter.ToBytes(file);
  public int[] Sizes { get; init; } = [];
  public NrrdType DataType { get; init; }
  public NrrdEncoding Encoding { get; init; }
  public string Endian { get; init; } = "little";
  public double[] Spacings { get; init; } = [];
  public byte[] PixelData { get; init; } = [];
  public string[] Labels { get; init; } = [];

  /// <summary>Converts this NRRD image to a <see cref="RawImage"/>, preserving 16-bit precision where possible.</summary>
  public RawImage ToRawImage() {
    var sizes = this.Sizes;
    if (sizes.Length < 2)
      throw new NotSupportedException("NRRD data must have at least 2 dimensions.");

    var src = this.PixelData;
    var isBigEndian = string.Equals(this.Endian, "big", StringComparison.OrdinalIgnoreCase);

    int width, height, channels;

    if (sizes.Length >= 2 && sizes[0] >= 1 && sizes[0] <= 4 && sizes.Length >= 3) {
      channels = sizes[0];
      width = sizes[1];
      height = sizes[2];
    } else {
      channels = 1;
      width = sizes[0];
      height = sizes[1];
    }

    var pixelCount = width * height;
    var bytesPerSample = _BytesPerSample(this.DataType);
    var is16Bit = _Is16BitDirect(this.DataType);
    var needsNormalize = _NeedsNormalize(this.DataType);

    if (channels == 1) {
      if (this.DataType == NrrdType.UInt8) {
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

      if (is16Bit) {
        var result = _DirectToGray16(src, pixelCount, bytesPerSample, isBigEndian, this.DataType);
        return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = result };
      }

      if (needsNormalize) {
        var result = _NormalizeChannelToGray16(src, pixelCount, bytesPerSample, isBigEndian, this.DataType);
        return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = result };
      }

      {
        var result = new byte[pixelCount];
        _NormalizeChannel(src, result, pixelCount, bytesPerSample, isBigEndian, this.DataType);
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
    }

    if (channels == 3) {
      if (this.DataType == NrrdType.UInt8) {
        var result = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; ++i) {
          var srcBase = i * 3;
          if (srcBase + 2 >= src.Length)
            break;

          var di = i * 3;
          result[di]     = src[srcBase];
          result[di + 1] = src[srcBase + 1];
          result[di + 2] = src[srcBase + 2];
        }

        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = result };
      }

      if (is16Bit || needsNormalize)
        return _ToRgb48MultiChannel(src, width, height, 3, bytesPerSample, isBigEndian, this.DataType, is16Bit);

      return _NormalizeMultiChannel(src, width, height, 3, bytesPerSample, isBigEndian, this.DataType);
    }

    if (channels == 4) {
      if (this.DataType == NrrdType.UInt8) {
        var result = new byte[pixelCount * 4];
        for (var i = 0; i < pixelCount; ++i) {
          var srcBase = i * 4;
          if (srcBase + 3 >= src.Length)
            break;

          var di = i * 4;
          result[di]     = src[srcBase];
          result[di + 1] = src[srcBase + 1];
          result[di + 2] = src[srcBase + 2];
          result[di + 3] = src[srcBase + 3];
        }

        return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = result };
      }

      if (is16Bit || needsNormalize)
        return _ToRgba64MultiChannel(src, width, height, 4, bytesPerSample, isBigEndian, this.DataType, is16Bit);

      return _NormalizeMultiChannel(src, width, height, 4, bytesPerSample, isBigEndian, this.DataType);
    }

    if (channels == 2) {
      var result = new byte[pixelCount];
      if (this.DataType == NrrdType.UInt8) {
        for (var i = 0; i < pixelCount && i * 2 < src.Length; ++i)
          result[i] = src[i * 2];
      } else {
        _NormalizeChannelStrided(src, result, pixelCount, bytesPerSample, 2, 0, isBigEndian, this.DataType);
      }

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

    throw new NotSupportedException($"NRRD with {channels} channels is not supported.");
  }

  /// <summary>NRRD encoding requires multi-dimensional data support and is not supported from 8-bit input.</summary>
  public static NrrdFile FromRawImage(RawImage image) => throw new NotSupportedException("NRRD encoding from RawImage is not supported because it requires multi-dimensional data support.");

  private static int _BytesPerSample(NrrdType type) => type switch {
    NrrdType.Int8 => 1,
    NrrdType.UInt8 => 1,
    NrrdType.Int16 => 2,
    NrrdType.UInt16 => 2,
    NrrdType.Int32 => 4,
    NrrdType.UInt32 => 4,
    NrrdType.Float => 4,
    NrrdType.Double => 8,
    _ => throw new NotSupportedException($"NRRD data type {type} is not supported.")
  };

  private static double _ReadSample(byte[] data, int offset, int bytesPerSample, bool isBigEndian, NrrdType type) {
    if (offset + bytesPerSample > data.Length)
      return 0;

    return type switch {
      NrrdType.Int8 => (sbyte)data[offset],
      NrrdType.UInt8 => data[offset],
      NrrdType.Int16 => isBigEndian ? (short)(data[offset] << 8 | data[offset + 1]) : (short)(data[offset] | data[offset + 1] << 8),
      NrrdType.UInt16 => isBigEndian ? (ushort)(data[offset] << 8 | data[offset + 1]) : (ushort)(data[offset] | data[offset + 1] << 8),
      NrrdType.Int32 => isBigEndian
        ? data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]
        : data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24,
      NrrdType.UInt32 => isBigEndian
        ? (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3])
        : (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24),
      NrrdType.Float => _ReadFloat(data, offset, isBigEndian),
      NrrdType.Double => _ReadDouble(data, offset, isBigEndian),
      _ => 0
    };
  }

  private static float _ReadFloat(byte[] data, int offset, bool isBigEndian) {
    if (!isBigEndian)
      return BitConverter.ToSingle(data, offset);

    Span<byte> buf = stackalloc byte[4];
    buf[0] = data[offset + 3];
    buf[1] = data[offset + 2];
    buf[2] = data[offset + 1];
    buf[3] = data[offset];
    return BitConverter.ToSingle(buf);
  }

  private static double _ReadDouble(byte[] data, int offset, bool isBigEndian) {
    if (!isBigEndian)
      return BitConverter.ToDouble(data, offset);

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

  private static void _NormalizeChannel(byte[] src, byte[] dst, int count, int bytesPerSample, bool isBigEndian, NrrdType type) {
    var isFloat = type == NrrdType.Float || type == NrrdType.Double;
    var min = double.MaxValue;
    var max = double.MinValue;

    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerSample;
      var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
      if (isFloat && (double.IsNaN(val) || double.IsInfinity(val)))
        continue;

      if (val < min) min = val;
      if (val > max) max = val;
    }

    var range = max - min;
    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerSample;
      var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
      if (isFloat && (double.IsNaN(val) || double.IsInfinity(val))) {
        dst[i] = 0;
        continue;
      }

      dst[i] = range == 0 ? (byte)0 : (byte)Math.Clamp((val - min) / range * 255.0, 0, 255);
    }
  }

  private static void _NormalizeChannelStrided(byte[] src, byte[] dst, int count, int bytesPerSample, int stride, int channelIndex, bool isBigEndian, NrrdType type) {
    var isFloat = type == NrrdType.Float || type == NrrdType.Double;
    var min = double.MaxValue;
    var max = double.MinValue;

    for (var i = 0; i < count; ++i) {
      var offset = (i * stride + channelIndex) * bytesPerSample;
      var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
      if (isFloat && (double.IsNaN(val) || double.IsInfinity(val)))
        continue;

      if (val < min) min = val;
      if (val > max) max = val;
    }

    var range = max - min;
    for (var i = 0; i < count; ++i) {
      var offset = (i * stride + channelIndex) * bytesPerSample;
      var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
      if (isFloat && (double.IsNaN(val) || double.IsInfinity(val))) {
        dst[i] = 0;
        continue;
      }

      dst[i] = range == 0 ? (byte)0 : (byte)Math.Clamp((val - min) / range * 255.0, 0, 255);
    }
  }

  private RawImage _NormalizeMultiChannel(byte[] src, int width, int height, int channels, int bytesPerSample, bool isBigEndian, NrrdType type) {
    var pixelCount = width * height;
    var isFloat = type == NrrdType.Float || type == NrrdType.Double;

    // Find min/max per channel
    var mins = new double[channels];
    var maxs = new double[channels];
    for (var c = 0; c < channels; ++c) {
      mins[c] = double.MaxValue;
      maxs[c] = double.MinValue;
    }

    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < channels; ++c) {
        var offset = (i * channels + c) * bytesPerSample;
        var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
        if (isFloat && (double.IsNaN(val) || double.IsInfinity(val)))
          continue;

        if (val < mins[c]) mins[c] = val;
        if (val > maxs[c]) maxs[c] = val;
      }

    var ranges = new double[channels];
    for (var c = 0; c < channels; ++c)
      ranges[c] = maxs[c] - mins[c];

    var result = new byte[pixelCount * channels];
    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < channels; ++c) {
        var offset = (i * channels + c) * bytesPerSample;
        var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
        if (isFloat && (double.IsNaN(val) || double.IsInfinity(val))) {
          result[i * channels + c] = 0;
          continue;
        }

        result[i * channels + c] = ranges[c] == 0 ? (byte)0 : (byte)Math.Clamp((val - mins[c]) / ranges[c] * 255.0, 0, 255);
      }

    return new() {
      Width = width,
      Height = height,
      Format = channels == 4 ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = result,
    };
  }

  private static bool _Is16BitDirect(NrrdType type) => type is NrrdType.Int16 or NrrdType.UInt16;

  private static bool _NeedsNormalize(NrrdType type) => type is NrrdType.Int32 or NrrdType.UInt32 or NrrdType.Float or NrrdType.Double or NrrdType.Int8;

  /// <summary>Converts Int16/UInt16 samples directly to Gray16 (big-endian uint16).</summary>
  private static byte[] _DirectToGray16(byte[] src, int count, int bytesPerSample, bool isBigEndian, NrrdType type) {
    var dst = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerSample;
      if (offset + bytesPerSample > src.Length)
        break;

      ushort u16;
      if (type == NrrdType.Int16) {
        var signed = isBigEndian
          ? (short)(src[offset] << 8 | src[offset + 1])
          : (short)(src[offset] | src[offset + 1] << 8);
        u16 = (ushort)(signed + 32768);
      } else {
        u16 = isBigEndian
          ? (ushort)(src[offset] << 8 | src[offset + 1])
          : (ushort)(src[offset] | src[offset + 1] << 8);
      }

      var di = i * 2;
      dst[di] = (byte)(u16 >> 8);
      dst[di + 1] = (byte)(u16 & 0xFF);
    }

    return dst;
  }

  /// <summary>Normalizes a single channel to Gray16 (big-endian uint16).</summary>
  private static byte[] _NormalizeChannelToGray16(byte[] src, int count, int bytesPerSample, bool isBigEndian, NrrdType type) {
    var isFloat = type == NrrdType.Float || type == NrrdType.Double;
    var min = double.MaxValue;
    var max = double.MinValue;

    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerSample;
      var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
      if (isFloat && (double.IsNaN(val) || double.IsInfinity(val)))
        continue;

      if (val < min) min = val;
      if (val > max) max = val;
    }

    var range = max - min;
    var dst = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerSample;
      var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
      ushort u16;
      if (isFloat && (double.IsNaN(val) || double.IsInfinity(val)))
        u16 = 0;
      else
        u16 = range == 0 ? (ushort)0 : (ushort)Math.Clamp((val - min) / range * 65535.0, 0, 65535);

      var di = i * 2;
      dst[di] = (byte)(u16 >> 8);
      dst[di + 1] = (byte)(u16 & 0xFF);
    }

    return dst;
  }

  /// <summary>Converts multi-channel data to Rgb48 (big-endian 16-bit per channel).</summary>
  private static RawImage _ToRgb48MultiChannel(byte[] src, int width, int height, int channels, int bytesPerSample, bool isBigEndian, NrrdType type, bool isDirect) {
    var pixelCount = width * height;
    var isFloat = type == NrrdType.Float || type == NrrdType.Double;

    if (isDirect) {
      var dst = new byte[pixelCount * 6];
      for (var i = 0; i < pixelCount; ++i)
        for (var c = 0; c < 3; ++c) {
          var offset = (i * channels + c) * bytesPerSample;
          ushort u16;
          if (type == NrrdType.Int16) {
            var signed = offset + 1 < src.Length
              ? isBigEndian ? (short)(src[offset] << 8 | src[offset + 1]) : (short)(src[offset] | src[offset + 1] << 8)
              : (short)0;
            u16 = (ushort)(signed + 32768);
          } else {
            u16 = offset + 1 < src.Length
              ? isBigEndian ? (ushort)(src[offset] << 8 | src[offset + 1]) : (ushort)(src[offset] | src[offset + 1] << 8)
              : (ushort)0;
          }

          var di = i * 6 + c * 2;
          dst[di] = (byte)(u16 >> 8);
          dst[di + 1] = (byte)(u16 & 0xFF);
        }

      return new() { Width = width, Height = height, Format = PixelFormat.Rgb48, PixelData = dst };
    }

    // Normalize to Rgb48
    var mins = new double[3];
    var maxs = new double[3];
    for (var c = 0; c < 3; ++c) { mins[c] = double.MaxValue; maxs[c] = double.MinValue; }

    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < 3; ++c) {
        var offset = (i * channels + c) * bytesPerSample;
        var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
        if (isFloat && (double.IsNaN(val) || double.IsInfinity(val)))
          continue;

        if (val < mins[c]) mins[c] = val;
        if (val > maxs[c]) maxs[c] = val;
      }

    var ranges = new double[3];
    for (var c = 0; c < 3; ++c) ranges[c] = maxs[c] - mins[c];

    var result = new byte[pixelCount * 6];
    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < 3; ++c) {
        var offset = (i * channels + c) * bytesPerSample;
        var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
        ushort u16;
        if (isFloat && (double.IsNaN(val) || double.IsInfinity(val)))
          u16 = 0;
        else
          u16 = ranges[c] == 0 ? (ushort)0 : (ushort)Math.Clamp((val - mins[c]) / ranges[c] * 65535.0, 0, 65535);

        var di = i * 6 + c * 2;
        result[di] = (byte)(u16 >> 8);
        result[di + 1] = (byte)(u16 & 0xFF);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb48, PixelData = result };
  }

  /// <summary>Converts multi-channel data to Rgba64 (big-endian 16-bit per channel).</summary>
  private static RawImage _ToRgba64MultiChannel(byte[] src, int width, int height, int channels, int bytesPerSample, bool isBigEndian, NrrdType type, bool isDirect) {
    var pixelCount = width * height;
    var isFloat = type == NrrdType.Float || type == NrrdType.Double;

    if (isDirect) {
      var dst = new byte[pixelCount * 8];
      for (var i = 0; i < pixelCount; ++i)
        for (var c = 0; c < 4; ++c) {
          var offset = (i * channels + c) * bytesPerSample;
          ushort u16;
          if (type == NrrdType.Int16) {
            var signed = offset + 1 < src.Length
              ? isBigEndian ? (short)(src[offset] << 8 | src[offset + 1]) : (short)(src[offset] | src[offset + 1] << 8)
              : (short)0;
            u16 = (ushort)(signed + 32768);
          } else {
            u16 = offset + 1 < src.Length
              ? isBigEndian ? (ushort)(src[offset] << 8 | src[offset + 1]) : (ushort)(src[offset] | src[offset + 1] << 8)
              : (ushort)0;
          }

          var di = i * 8 + c * 2;
          dst[di] = (byte)(u16 >> 8);
          dst[di + 1] = (byte)(u16 & 0xFF);
        }

      return new() { Width = width, Height = height, Format = PixelFormat.Rgba64, PixelData = dst };
    }

    // Normalize to Rgba64
    var mins = new double[4];
    var maxs = new double[4];
    for (var c = 0; c < 4; ++c) { mins[c] = double.MaxValue; maxs[c] = double.MinValue; }

    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < 4; ++c) {
        var offset = (i * channels + c) * bytesPerSample;
        var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
        if (isFloat && (double.IsNaN(val) || double.IsInfinity(val)))
          continue;

        if (val < mins[c]) mins[c] = val;
        if (val > maxs[c]) maxs[c] = val;
      }

    var ranges = new double[4];
    for (var c = 0; c < 4; ++c) ranges[c] = maxs[c] - mins[c];

    var result = new byte[pixelCount * 8];
    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < 4; ++c) {
        var offset = (i * channels + c) * bytesPerSample;
        var val = _ReadSample(src, offset, bytesPerSample, isBigEndian, type);
        ushort u16;
        if (isFloat && (double.IsNaN(val) || double.IsInfinity(val)))
          u16 = 0;
        else
          u16 = ranges[c] == 0 ? (ushort)0 : (ushort)Math.Clamp((val - mins[c]) / ranges[c] * 65535.0, 0, 65535);

        var di = i * 8 + c * 2;
        result[di] = (byte)(u16 >> 8);
        result[di + 1] = (byte)(u16 & 0xFF);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba64, PixelData = result };
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
