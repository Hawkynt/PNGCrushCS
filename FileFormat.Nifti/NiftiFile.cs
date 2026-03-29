using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Nifti;

/// <summary>In-memory representation of a NIfTI neuroimaging file.</summary>
public sealed class NiftiFile : IImageFileFormat<NiftiFile> {

  static string IImageFileFormat<NiftiFile>.PrimaryExtension => ".nii";
  static string[] IImageFileFormat<NiftiFile>.FileExtensions => [".nii"];
  static NiftiFile IImageFileFormat<NiftiFile>.FromFile(FileInfo file) => NiftiReader.FromFile(file);
  static NiftiFile IImageFileFormat<NiftiFile>.FromBytes(byte[] data) => NiftiReader.FromBytes(data);
  static NiftiFile IImageFileFormat<NiftiFile>.FromStream(Stream stream) => NiftiReader.FromStream(stream);
  static RawImage IImageFileFormat<NiftiFile>.ToRawImage(NiftiFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<NiftiFile>.ToBytes(NiftiFile file) => NiftiWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Depth { get; init; }
  public NiftiDataType Datatype { get; init; }
  public short Bitpix { get; init; }
  public float SclSlope { get; init; }
  public float SclInter { get; init; }
  public float VoxOffset { get; init; }
  public string Description { get; init; } = "";

  /// <summary>Raw voxel data starting at VoxOffset.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Voxel dimensions (up to 8 entries matching pixdim[0..7]).</summary>
  public float[] Pixdim { get; init; } = [];

  /// <summary>Converts the first 2D slice of this NIfTI volume to a <see cref="RawImage"/>, preserving 16-bit precision where possible.</summary>
  public RawImage ToRawImage() {
    var width = this.Width;
    var height = this.Height;
    var src = this.PixelData;
    var pixelCount = width * height;
    var slope = this.SclSlope;
    var inter = this.SclInter;
    var useScaling = slope != 0.0f && slope != 1.0f || inter != 0.0f;

    if (this.Datatype == NiftiDataType.Rgb24) {
      var bytesNeeded = pixelCount * 3;
      var result = new byte[bytesNeeded];
      Buffer.BlockCopy(src, 0, result, 0, Math.Min(src.Length, bytesNeeded));
      return new() {
        Width = width,
        Height = height,
        Format = PixelFormat.Rgb24,
        PixelData = result,
      };
    }

    // 8-bit types without scaling stay at 8-bit
    if (!useScaling)
      switch (this.Datatype) {
        case NiftiDataType.UInt8: {
          var output = new byte[pixelCount];
          Buffer.BlockCopy(src, 0, output, 0, Math.Min(src.Length, pixelCount));
          var palette = _BuildGrayscalePalette();
          return new() {
            Width = width,
            Height = height,
            Format = PixelFormat.Indexed8,
            PixelData = output,
            Palette = palette,
            PaletteCount = 256,
          };
        }
        case NiftiDataType.Int8: {
          var output = new byte[pixelCount];
          for (var i = 0; i < pixelCount && i < src.Length; ++i)
            output[i] = (byte)((sbyte)src[i] + 128);

          var palette = _BuildGrayscalePalette();
          return new() {
            Width = width,
            Height = height,
            Format = PixelFormat.Indexed8,
            PixelData = output,
            Palette = palette,
            PaletteCount = 256,
          };
        }
        case NiftiDataType.Int16: {
          var output = _Int16LEToGray16(src, pixelCount);
          return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = output };
        }
        case NiftiDataType.UInt16: {
          var output = _UInt16LEToGray16(src, pixelCount);
          return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = output };
        }
      }

    // All remaining types (>8-bit, scaled 8-bit, floats) normalize to Gray16
    var result16 = new byte[pixelCount * 2];

    switch (this.Datatype) {
      case NiftiDataType.UInt8:
        _NormalizeToGray16WithScaling(src, result16, pixelCount, 1, i => src[i], slope, inter);
        break;
      case NiftiDataType.Int8:
        _NormalizeToGray16WithScaling(src, result16, pixelCount, 1, i => (sbyte)src[i], slope, inter);
        break;
      case NiftiDataType.Int16:
        _NormalizeToGray16LE(src, result16, pixelCount, 2, offset => (short)(src[offset] | src[offset + 1] << 8), slope, inter, useScaling);
        break;
      case NiftiDataType.UInt16:
        _NormalizeToGray16LE(src, result16, pixelCount, 2, offset => (ushort)(src[offset] | src[offset + 1] << 8), slope, inter, useScaling);
        break;
      case NiftiDataType.Int32:
        _NormalizeToGray16LE(src, result16, pixelCount, 4, offset => src[offset] | src[offset + 1] << 8 | src[offset + 2] << 16 | src[offset + 3] << 24, slope, inter, useScaling);
        break;
      case NiftiDataType.UInt32:
        _NormalizeToGray16LE(src, result16, pixelCount, 4, offset => (uint)(src[offset] | src[offset + 1] << 8 | src[offset + 2] << 16 | src[offset + 3] << 24), slope, inter, useScaling);
        break;
      case NiftiDataType.Float32:
        _NormalizeFloatToGray16LE(src, result16, pixelCount, 4, offset => BitConverter.ToSingle(src, offset), slope, inter, useScaling);
        break;
      case NiftiDataType.Float64:
        _NormalizeFloatToGray16LE(src, result16, pixelCount, 8, offset => BitConverter.ToDouble(src, offset), slope, inter, useScaling);
        break;
      default:
        throw new NotSupportedException($"NIfTI data type {this.Datatype} is not supported.");
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Gray16,
      PixelData = result16,
    };
  }

  /// <summary>NIfTI encoding requires specific voxel data types and 3D volume support.</summary>
  public static NiftiFile FromRawImage(RawImage image) => throw new NotSupportedException("NIfTI encoding from RawImage is not supported because it requires specific voxel data types and 3D volume support.");

  /// <summary>Converts little-endian signed Int16 to Gray16 (big-endian uint16) by offsetting by 32768.</summary>
  private static byte[] _Int16LEToGray16(byte[] src, int count) {
    var dst = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      var offset = i * 2;
      if (offset + 1 >= src.Length)
        break;

      var signed = (short)(src[offset] | src[offset + 1] << 8);
      var unsigned = (ushort)(signed + 32768);
      var di = i * 2;
      dst[di] = (byte)(unsigned >> 8);
      dst[di + 1] = (byte)(unsigned & 0xFF);
    }

    return dst;
  }

  /// <summary>Converts little-endian unsigned UInt16 to Gray16 (big-endian uint16).</summary>
  private static byte[] _UInt16LEToGray16(byte[] src, int count) {
    var dst = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      var offset = i * 2;
      if (offset + 1 >= src.Length)
        break;

      var val = (ushort)(src[offset] | src[offset + 1] << 8);
      var di = i * 2;
      dst[di] = (byte)(val >> 8);
      dst[di + 1] = (byte)(val & 0xFF);
    }

    return dst;
  }

  private static void _NormalizeToGray16WithScaling(byte[] src, byte[] dst, int count, int bytesPerVoxel, Func<int, double> readRaw, float slope, float inter) {
    var min = double.MaxValue;
    var max = double.MinValue;
    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerVoxel;
      if (offset + bytesPerVoxel > src.Length)
        break;

      var val = readRaw(offset) * slope + inter;
      if (val < min) min = val;
      if (val > max) max = val;
    }

    var range = max - min;
    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerVoxel;
      if (offset + bytesPerVoxel > src.Length)
        break;

      var val = readRaw(offset) * slope + inter;
      var u16 = range == 0 ? (ushort)0 : (ushort)Math.Clamp((val - min) / range * 65535.0, 0, 65535);
      var di = i * 2;
      dst[di] = (byte)(u16 >> 8);
      dst[di + 1] = (byte)(u16 & 0xFF);
    }
  }

  private static void _NormalizeToGray16LE<T>(byte[] src, byte[] dst, int count, int bytesPerVoxel, Func<int, T> readValue, float slope, float inter, bool useScaling) where T : struct, IConvertible {
    var min = double.MaxValue;
    var max = double.MinValue;
    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerVoxel;
      if (offset + bytesPerVoxel > src.Length)
        break;

      double val = readValue(offset).ToDouble(null);
      if (useScaling)
        val = val * slope + inter;

      if (val < min) min = val;
      if (val > max) max = val;
    }

    var range = max - min;
    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerVoxel;
      if (offset + bytesPerVoxel > src.Length)
        break;

      double val = readValue(offset).ToDouble(null);
      if (useScaling)
        val = val * slope + inter;

      var u16 = range == 0 ? (ushort)0 : (ushort)Math.Clamp((val - min) / range * 65535.0, 0, 65535);
      var di = i * 2;
      dst[di] = (byte)(u16 >> 8);
      dst[di + 1] = (byte)(u16 & 0xFF);
    }
  }

  private static void _NormalizeFloatToGray16LE(byte[] src, byte[] dst, int count, int bytesPerVoxel, Func<int, double> readValue, float slope, float inter, bool useScaling) {
    var min = double.MaxValue;
    var max = double.MinValue;
    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerVoxel;
      if (offset + bytesPerVoxel > src.Length)
        break;

      var val = readValue(offset);
      if (double.IsNaN(val) || double.IsInfinity(val))
        continue;

      if (useScaling)
        val = val * slope + inter;

      if (val < min) min = val;
      if (val > max) max = val;
    }

    var range = max - min;
    for (var i = 0; i < count; ++i) {
      var offset = i * bytesPerVoxel;
      if (offset + bytesPerVoxel > src.Length)
        break;

      var val = readValue(offset);
      ushort u16;
      if (double.IsNaN(val) || double.IsInfinity(val)) {
        u16 = 0;
      } else {
        if (useScaling)
          val = val * slope + inter;

        u16 = range == 0 ? (ushort)0 : (ushort)Math.Clamp((val - min) / range * 65535.0, 0, 65535);
      }

      var di = i * 2;
      dst[di] = (byte)(u16 >> 8);
      dst[di + 1] = (byte)(u16 & 0xFF);
    }
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
