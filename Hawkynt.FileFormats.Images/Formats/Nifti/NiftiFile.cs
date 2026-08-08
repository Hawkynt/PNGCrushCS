using System;
using FileFormat.Core;

namespace FileFormat.Nifti;

/// <summary>In-memory representation of a NIfTI neuroimaging file.</summary>
public sealed class NiftiFile :
  IImageFormatReader<NiftiFile>, IImageToRawImage<NiftiFile>,
  IImageFromRawImage<NiftiFile>, IImageFormatWriter<NiftiFile> {

  static string IImageFormatMetadata<NiftiFile>.PrimaryExtension => ".nii";
  static string[] IImageFormatMetadata<NiftiFile>.FileExtensions => [".nii"];
  static NiftiFile IImageFormatReader<NiftiFile>.FromSpan(ReadOnlySpan<byte> data) => NiftiReader.FromSpan(data);
  static byte[] IImageFormatWriter<NiftiFile>.ToBytes(NiftiFile file) => NiftiWriter.ToBytes(file);

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

  /// <summary>The keyword the 80-character <c>descrip</c> field is carried under.</summary>
  /// <remarks>
  /// It is what a scanner or a pipeline wrote about the study, so it belongs with the picture rather
  /// than being dropped the moment the voxels are converted. "Description" is one of PNG's own
  /// keywords, so it survives a hop through any format that keeps text.
  /// </remarks>
  private const string _DESCRIPTION_KEYWORD = "Description";

  /// <summary>Converts the first 2D slice of this NIfTI volume to a <see cref="RawImage"/>, preserving 16-bit precision where possible.</summary>
  public static RawImage ToRawImage(NiftiFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var image = _Decode(file);
    var description = file.Description?.Trim('\0').Trim();
    if (string.IsNullOrEmpty(description))
      return image;

    return new() {
      Width = image.Width,
      Height = image.Height,
      Format = image.Format,
      PixelData = image.PixelData,
      Palette = image.Palette,
      PaletteCount = image.PaletteCount,
      AlphaTable = image.AlphaTable,
      Metadata = new() { TextEntries = [new(_DESCRIPTION_KEYWORD, description)] },
    };
  }

  private static RawImage _Decode(NiftiFile file) {
    var width = file.Width;
    var height = file.Height;
    var src = file.PixelData;
    var pixelCount = width * height;
    var slope = file.SclSlope;
    var inter = file.SclInter;
    var useScaling = slope != 0.0f && slope != 1.0f || inter != 0.0f;

    if (file.Datatype == NiftiDataType.Rgb24) {
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
      switch (file.Datatype) {
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

    switch (file.Datatype) {
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
        throw new NotSupportedException($"NIfTI data type {file.Datatype} is not supported.");
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Gray16,
      PixelData = result16,
    };
  }

  /// <summary>Writes a picture as a single-slice NIfTI volume, keeping whatever precision it arrived with.</summary>
  /// <remarks>
  /// A scan is measurement rather than picture, so the depth it was taken at is the thing worth
  /// keeping: sixteen-bit grey is stored as <see cref="NiftiDataType.UInt16"/> voxels rather than
  /// being crushed to eight, and colour is stored as <see cref="NiftiDataType.Rgb24"/>, which is the
  /// only colour voxel type NIfTI-1 has. The dimensions live in the header, so any size fits.
  /// <para/>
  /// Voxels are little-endian, which is what <c>ToRawImage</c> reads them back as — the
  /// <see cref="PixelFormat.Gray16"/> buffer it hands out is big-endian, so the two orders are
  /// swapped here rather than copied.
  /// </remarks>
  public static NiftiFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var pixelCount = image.Width * image.Height;
    var (datatype, bitpix, voxels) = image.Format switch {
      PixelFormat.Gray16 or PixelFormat.Gray10
        => (NiftiDataType.UInt16, (short)16, _Gray16ToUInt16LE(image.EnsureFormat(PixelFormat.Gray16).PixelData, pixelCount)),
      PixelFormat.Gray8 or PixelFormat.GrayAlpha16
        => (NiftiDataType.UInt8, (short)8, image.EnsureFormat(PixelFormat.Gray8).PixelData[..pixelCount]),
      _ => (NiftiDataType.Rgb24, (short)24, image.EnsureFormat(PixelFormat.Rgb24).PixelData[..(pixelCount * 3)])
    };

    return new() {
      Width = image.Width,
      Height = image.Height,
      Depth = 1,
      Datatype = datatype,
      Bitpix = bitpix,
      SclSlope = 1f,
      SclInter = 0f,
      VoxOffset = 352f,
      Pixdim = [1f, 1f, 1f, 1f, 0f, 0f, 0f, 0f],
      Description = _DescriptionFrom(image.Metadata),
      PixelData = voxels,
    };
  }

  /// <summary>The text that belongs in <c>descrip</c>, trimmed to the 80 characters it holds.</summary>
  /// <remarks>
  /// A picture arriving from a format with keyword-tagged text is searched for the same keyword
  /// this writes out; one carrying a single bare annotation — a JPEG comment, say — has nothing
  /// else it could mean, so that is taken instead.
  /// </remarks>
  private static string _DescriptionFrom(ImageMetadata? metadata) {
    if (metadata is not { TextEntries.Count: > 0 } present)
      return "";

    var text = "";
    foreach (var entry in present.TextEntries)
      if (string.Equals(entry.Keyword, _DESCRIPTION_KEYWORD, StringComparison.OrdinalIgnoreCase)) {
        text = entry.Text;
        break;
      }

    if (text.Length == 0 && present.TextEntries.Count == 1 && present.TextEntries[0].Keyword.Length == 0)
      text = present.TextEntries[0].Text;

    return text.Length > 80 ? text[..80] : text;
  }

  /// <summary>Turns the big-endian words of a <see cref="PixelFormat.Gray16"/> buffer into the file's little-endian voxels.</summary>
  private static byte[] _Gray16ToUInt16LE(byte[] gray16, int count) {
    var voxels = new byte[count * 2];
    for (var i = 0; i < count; ++i) {
      voxels[i * 2] = gray16[i * 2 + 1];
      voxels[i * 2 + 1] = gray16[i * 2];
    }

    return voxels;
  }

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
