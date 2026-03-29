using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Envi;

/// <summary>In-memory representation of an ENVI remote sensing image.</summary>
public sealed class EnviFile : IImageFileFormat<EnviFile> {

  static string IImageFileFormat<EnviFile>.PrimaryExtension => ".hdr";
  static string[] IImageFileFormat<EnviFile>.FileExtensions => [".hdr"];
  static EnviFile IImageFileFormat<EnviFile>.FromFile(FileInfo file) => EnviReader.FromFile(file);
  static EnviFile IImageFileFormat<EnviFile>.FromBytes(byte[] data) => EnviReader.FromBytes(data);
  static EnviFile IImageFileFormat<EnviFile>.FromStream(Stream stream) => EnviReader.FromStream(stream);
  static byte[] IImageFileFormat<EnviFile>.ToBytes(EnviFile file) => EnviWriter.ToBytes(file);

  static bool? IImageFileFormat<EnviFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 5 && header[0] == 0x45 && header[1] == 0x4E && header[2] == 0x56 && header[3] == 0x49
      && (header[4] == 0x0D || header[4] == 0x0A)
      ? true : null;

  /// <summary>Image width in pixels (samples per line).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (lines).</summary>
  public int Height { get; init; }

  /// <summary>Number of spectral bands.</summary>
  public int Bands { get; init; } = 1;

  /// <summary>ENVI data type code (1=uint8, 2=int16, 4=float32, 12=uint16).</summary>
  public int DataType { get; init; } = 1;

  /// <summary>Band interleave organization.</summary>
  public EnviInterleave Interleave { get; init; }

  /// <summary>Byte order: 0=little-endian, 1=big-endian.</summary>
  public int ByteOrder { get; init; }

  /// <summary>Raw pixel data bytes.</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(EnviFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var dataType = file.DataType;
    var bands = file.Bands;
    var width = file.Width;
    var height = file.Height;
    var pixelCount = width * height;
    var isLE = file.ByteOrder == 0;

    // uint8 paths (8-bit output)
    if (dataType == 1) {
      if (bands == 1)
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Gray8,
          PixelData = file.PixelData[..],
        };

      if (bands >= 3) {
        var result = _DeinterleaveUInt8(file, pixelCount);
        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = result };
      }
    }

    // int16 (data_type=2): offset by 32768 -> uint16, output as Gray16/Rgb48
    if (dataType == 2) {
      if (bands == 1) {
        var result = _ConvertInt16ToGray16(file.PixelData, pixelCount, isLE);
        return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = result };
      }

      if (bands >= 3) {
        var result = _ConvertInt16ToRgb48(file, pixelCount, isLE);
        return new() { Width = width, Height = height, Format = PixelFormat.Rgb48, PixelData = result };
      }
    }

    // uint16 (data_type=12): direct to Gray16/Rgb48
    if (dataType == 12) {
      if (bands == 1) {
        var result = _ConvertUInt16ToGray16(file.PixelData, pixelCount, isLE);
        return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = result };
      }

      if (bands >= 3) {
        var result = _ConvertUInt16ToRgb48(file, pixelCount, isLE);
        return new() { Width = width, Height = height, Format = PixelFormat.Rgb48, PixelData = result };
      }
    }

    // float32 (data_type=4): normalize to [0,65535], output as Gray16/Rgb48
    if (dataType == 4) {
      if (bands == 1) {
        var result = _NormalizeFloat32ToGray16(file.PixelData, pixelCount, isLE);
        return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = result };
      }

      if (bands >= 3) {
        var result = _NormalizeFloat32ToRgb48(file, pixelCount, isLE);
        return new() { Width = width, Height = height, Format = PixelFormat.Rgb48, PixelData = result };
      }
    }

    throw new ArgumentException($"Unsupported ENVI configuration: data_type={dataType}, bands={bands}.", nameof(file));
  }

  private static byte[] _DeinterleaveUInt8(EnviFile file, int pixelCount) {
    var result = new byte[pixelCount * 3];
    switch (file.Interleave) {
      case EnviInterleave.Bip:
        if (file.Bands == 3)
          result = file.PixelData[..];
        else
          for (var i = 0; i < pixelCount; ++i) {
            var srcOffset = i * file.Bands;
            result[i * 3] = file.PixelData[srcOffset];
            result[i * 3 + 1] = file.PixelData[srcOffset + 1];
            result[i * 3 + 2] = file.PixelData[srcOffset + 2];
          }

        break;
      case EnviInterleave.Bsq:
        result = PixelConverter.BandSequentialToInterleaved(file.PixelData, pixelCount, 3);
        break;
      case EnviInterleave.Bil:
        var w = file.Width;
        for (var y = 0; y < file.Height; ++y) {
          var lineOffset = y * w * file.Bands;
          for (var x = 0; x < w; ++x) {
            result[(y * w + x) * 3] = file.PixelData[lineOffset + x];
            result[(y * w + x) * 3 + 1] = file.PixelData[lineOffset + w + x];
            result[(y * w + x) * 3 + 2] = file.PixelData[lineOffset + w * 2 + x];
          }
        }

        break;
      default:
        throw new ArgumentException($"Unsupported interleave: {file.Interleave}", nameof(file));
    }

    return result;
  }

  private static ushort _ReadUInt16(byte[] data, int offset, bool isLE)
    => isLE ? (ushort)(data[offset] | data[offset + 1] << 8) : (ushort)(data[offset] << 8 | data[offset + 1]);

  private static short _ReadInt16(byte[] data, int offset, bool isLE)
    => isLE ? (short)(data[offset] | data[offset + 1] << 8) : (short)(data[offset] << 8 | data[offset + 1]);

  private static float _ReadFloat32(byte[] data, int offset, bool isLE) {
    if (isLE)
      return BitConverter.ToSingle(data, offset);

    Span<byte> buf = stackalloc byte[4];
    buf[0] = data[offset + 3];
    buf[1] = data[offset + 2];
    buf[2] = data[offset + 1];
    buf[3] = data[offset];
    return BitConverter.ToSingle(buf);
  }

  private static void _WriteGray16BE(byte[] dst, int di, ushort value) {
    dst[di] = (byte)(value >> 8);
    dst[di + 1] = (byte)(value & 0xFF);
  }

  private static byte[] _ConvertInt16ToGray16(byte[] src, int count, bool isLE) {
    var dst = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      var offset = i * 2;
      if (offset + 1 >= src.Length)
        break;

      var signed = _ReadInt16(src, offset, isLE);
      _WriteGray16BE(dst, i * 2, (ushort)(signed + 32768));
    }

    return dst;
  }

  private static byte[] _ConvertUInt16ToGray16(byte[] src, int count, bool isLE) {
    var dst = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      var offset = i * 2;
      if (offset + 1 >= src.Length)
        break;

      _WriteGray16BE(dst, i * 2, _ReadUInt16(src, offset, isLE));
    }

    return dst;
  }

  private static byte[] _NormalizeFloat32ToGray16(byte[] src, int count, bool isLE) {
    var min = float.MaxValue;
    var max = float.MinValue;
    for (var i = 0; i < count; ++i) {
      var offset = i * 4;
      if (offset + 3 >= src.Length)
        break;

      var val = _ReadFloat32(src, offset, isLE);
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

      var val = _ReadFloat32(src, offset, isLE);
      ushort u16;
      if (float.IsNaN(val) || float.IsInfinity(val))
        u16 = 0;
      else
        u16 = range == 0 ? (ushort)0 : (ushort)Math.Clamp((val - min) / range * 65535.0f, 0, 65535);

      _WriteGray16BE(dst, i * 2, u16);
    }

    return dst;
  }

  /// <summary>Computes the byte offset of a 16-bit sample at a given pixel/channel based on the file's interleave mode.</summary>
  private static int _SampleOffset16(EnviFile file, int pixelIndex, int channel, int pixelCount) {
    var bands = file.Bands;
    var w = file.Width;
    return file.Interleave switch {
      EnviInterleave.Bip => (pixelIndex * bands + channel) * 2,
      EnviInterleave.Bsq => (channel * pixelCount + pixelIndex) * 2,
      EnviInterleave.Bil => (pixelIndex / w * w * bands + channel * w + pixelIndex % w) * 2,
      _ => throw new ArgumentException($"Unsupported interleave: {file.Interleave}"),
    };
  }

  /// <summary>Computes the byte offset of a float32 sample at a given pixel/channel based on the file's interleave mode.</summary>
  private static int _SampleOffsetFloat(EnviFile file, int pixelIndex, int channel, int pixelCount) {
    var bands = file.Bands;
    var w = file.Width;
    return file.Interleave switch {
      EnviInterleave.Bip => (pixelIndex * bands + channel) * 4,
      EnviInterleave.Bsq => (channel * pixelCount + pixelIndex) * 4,
      EnviInterleave.Bil => (pixelIndex / w * w * bands + channel * w + pixelIndex % w) * 4,
      _ => throw new ArgumentException($"Unsupported interleave: {file.Interleave}"),
    };
  }

  private static byte[] _ConvertInt16ToRgb48(EnviFile file, int pixelCount, bool isLE) {
    var dst = new byte[pixelCount * 6];
    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < 3; ++c) {
        var offset = _SampleOffset16(file, i, c, pixelCount);
        if (offset + 1 >= file.PixelData.Length)
          continue;

        var signed = _ReadInt16(file.PixelData, offset, isLE);
        _WriteGray16BE(dst, i * 6 + c * 2, (ushort)(signed + 32768));
      }

    return dst;
  }

  private static byte[] _ConvertUInt16ToRgb48(EnviFile file, int pixelCount, bool isLE) {
    var dst = new byte[pixelCount * 6];
    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < 3; ++c) {
        var offset = _SampleOffset16(file, i, c, pixelCount);
        if (offset + 1 >= file.PixelData.Length)
          continue;

        _WriteGray16BE(dst, i * 6 + c * 2, _ReadUInt16(file.PixelData, offset, isLE));
      }

    return dst;
  }

  private static byte[] _NormalizeFloat32ToRgb48(EnviFile file, int pixelCount, bool isLE) {
    var mins = new float[3];
    var maxs = new float[3];
    for (var c = 0; c < 3; ++c) { mins[c] = float.MaxValue; maxs[c] = float.MinValue; }

    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < 3; ++c) {
        var offset = _SampleOffsetFloat(file, i, c, pixelCount);
        if (offset + 3 >= file.PixelData.Length)
          continue;

        var val = _ReadFloat32(file.PixelData, offset, isLE);
        if (float.IsNaN(val) || float.IsInfinity(val))
          continue;

        if (val < mins[c]) mins[c] = val;
        if (val > maxs[c]) maxs[c] = val;
      }

    var ranges = new float[3];
    for (var c = 0; c < 3; ++c) ranges[c] = maxs[c] - mins[c];

    var dst = new byte[pixelCount * 6];
    for (var i = 0; i < pixelCount; ++i)
      for (var c = 0; c < 3; ++c) {
        var offset = _SampleOffsetFloat(file, i, c, pixelCount);
        if (offset + 3 >= file.PixelData.Length)
          continue;

        var val = _ReadFloat32(file.PixelData, offset, isLE);
        ushort u16;
        if (float.IsNaN(val) || float.IsInfinity(val))
          u16 = 0;
        else
          u16 = ranges[c] == 0 ? (ushort)0 : (ushort)Math.Clamp((val - mins[c]) / ranges[c] * 65535.0f, 0, 65535);

        _WriteGray16BE(dst, i * 6 + c * 2, u16);
      }

    return dst;
  }

  public static EnviFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    switch (image.Format) {
      case PixelFormat.Gray8:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 1,
          DataType = 1,
          Interleave = EnviInterleave.Bsq,
          ByteOrder = 0,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 3,
          DataType = 1,
          Interleave = EnviInterleave.Bip,
          ByteOrder = 0,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Gray16: {
        // Gray16 is big-endian uint16; write as ENVI data_type=12 (uint16) big-endian
        var pixelCount = image.Width * image.Height;
        var pixelData = new byte[pixelCount * 2];
        Buffer.BlockCopy(image.PixelData, 0, pixelData, 0, Math.Min(image.PixelData.Length, pixelData.Length));
        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 1,
          DataType = 12,
          Interleave = EnviInterleave.Bsq,
          ByteOrder = 1,
          PixelData = pixelData,
        };
      }
      case PixelFormat.Rgb48: {
        // Rgb48 is big-endian 16-bit per channel; write as ENVI data_type=12 (uint16) big-endian BIP
        var pixelCount = image.Width * image.Height;
        var pixelData = new byte[pixelCount * 6];
        Buffer.BlockCopy(image.PixelData, 0, pixelData, 0, Math.Min(image.PixelData.Length, pixelData.Length));
        return new() {
          Width = image.Width,
          Height = image.Height,
          Bands = 3,
          DataType = 12,
          Interleave = EnviInterleave.Bip,
          ByteOrder = 1,
          PixelData = pixelData,
        };
      }
      default:
        throw new ArgumentException($"Unsupported pixel format for ENVI: {image.Format}", nameof(image));
    }
  }
}
