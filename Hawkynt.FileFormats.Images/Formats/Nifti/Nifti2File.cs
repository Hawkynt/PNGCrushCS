using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Nifti;

/// <summary>NIfTI-2 single-file form with 64-bit dimensions and offsets.</summary>
[FormatDetectionPriority(80)]
public sealed class Nifti2File :
  IImageFormatReader<Nifti2File>, IImageToRawImage<Nifti2File>,
  IImageFromRawImage<Nifti2File>, IImageFormatWriter<Nifti2File> {

  static string IImageFormatMetadata<Nifti2File>.PrimaryExtension => ".nii";
  static string[] IImageFormatMetadata<Nifti2File>.FileExtensions => [".nii"];
  static Nifti2File IImageFormatReader<Nifti2File>.FromSpan(ReadOnlySpan<byte> data) => Nifti2Reader.FromSpan(data);
  static byte[] IImageFormatWriter<Nifti2File>.ToBytes(Nifti2File file) => Nifti2Writer.ToBytes(file);

  public NiftiFile Nifti { get; init; } = new();

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => Nifti2Codec.Matches(header, pair: false);

  public static RawImage ToRawImage(Nifti2File file) {
    ArgumentNullException.ThrowIfNull(file);
    return NiftiFile.ToRawImage(file.Nifti);
  }

  public static Nifti2File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Nifti = NiftiFile.FromRawImage(image) };
  }
}

public static class Nifti2Reader {
  public static Nifti2File FromSpan(ReadOnlySpan<byte> data)
    => new() { Nifti = Nifti2Codec.ParseSingle(data) };
}

public static class Nifti2Writer {
  public static byte[] ToBytes(Nifti2File file) {
    ArgumentNullException.ThrowIfNull(file);
    return Nifti2Codec.WriteSingle(file.Nifti);
  }
}

/// <summary>GZip-wrapped NIfTI-2 single-file form.</summary>
[FormatDetectionPriority(80)]
public sealed class Nifti2GzipFile :
  IImageFormatReader<Nifti2GzipFile>, IImageToRawImage<Nifti2GzipFile>,
  IImageFromRawImage<Nifti2GzipFile>, IImageFormatWriter<Nifti2GzipFile> {

  static string IImageFormatMetadata<Nifti2GzipFile>.PrimaryExtension => ".nii.gz";
  static string[] IImageFormatMetadata<Nifti2GzipFile>.FileExtensions => [".nii.gz"];
  static Nifti2GzipFile IImageFormatReader<Nifti2GzipFile>.FromSpan(ReadOnlySpan<byte> data) => Nifti2GzipReader.FromSpan(data);
  static byte[] IImageFormatWriter<Nifti2GzipFile>.ToBytes(Nifti2GzipFile file) => Nifti2GzipWriter.ToBytes(file);

  public NiftiFile Nifti { get; init; } = new();

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 2 || header[0] != 0x1F || header[1] != 0x8B)
      return false;
    try {
      var raw = _Decompress(header);
      return Nifti2Codec.Matches(raw, pair: false);
    } catch {
      return null;
    }
  }

  public static RawImage ToRawImage(Nifti2GzipFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return NiftiFile.ToRawImage(file.Nifti);
  }

  public static Nifti2GzipFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    return new() { Nifti = NiftiFile.FromRawImage(image) };
  }

  internal static byte[] _Decompress(ReadOnlySpan<byte> data) {
    using var input = new MemoryStream(data.ToArray(), writable: false);
    using var gzip = new GZipStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    gzip.CopyTo(output);
    return output.ToArray();
  }
}

public static class Nifti2GzipReader {
  public static Nifti2GzipFile FromSpan(ReadOnlySpan<byte> data)
    => new() { Nifti = Nifti2Codec.ParseSingle(Nifti2GzipFile._Decompress(data)) };
}

public static class Nifti2GzipWriter {
  public static byte[] ToBytes(Nifti2GzipFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var raw = Nifti2Codec.WriteSingle(file.Nifti);
    using var output = new MemoryStream();
    using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
      gzip.Write(raw, 0, raw.Length);
    return output.ToArray();
  }
}

/// <summary>Wire-level NIfTI-2 header codec. Only display-relevant fields are projected into <see cref="NiftiFile"/>.</summary>
internal static class Nifti2Codec {
  internal const int HeaderSize = 540;
  internal const int SingleDataOffset = 544;

  private static ReadOnlySpan<byte> SingleMagic => "n+2\0\r\n\x1A\n"u8;
  private static ReadOnlySpan<byte> PairMagic => "ni2\0\r\n\x1A\n"u8;

  internal static bool? Matches(ReadOnlySpan<byte> data, bool pair) {
    if (data.Length < 12)
      return null;

    var little = BinaryPrimitives.ReadInt32LittleEndian(data);
    var big = BinaryPrimitives.ReadInt32BigEndian(data);
    if (little != HeaderSize && big != HeaderSize)
      return null;

    var expected = pair ? PairMagic : SingleMagic;
    return data.Slice(4, 8).SequenceEqual(expected) ? true : null;
  }

  internal static NiftiFile ParseSingle(ReadOnlySpan<byte> data) {
    if (data.Length < HeaderSize)
      throw new InvalidDataException("Data is too small for a NIfTI-2 header.");

    var isBigEndian = _IsBigEndian(data);
    if (!data.Slice(4, 8).SequenceEqual(SingleMagic))
      throw new InvalidDataException("Invalid NIfTI-2 single-file magic.");

    var datatype = (NiftiDataType)_ReadInt16(data, 12, isBigEndian);
    var bitpix = _ReadInt16(data, 14, isBigEndian);
    var ndims = _ReadInt64(data, 16, isBigEndian);
    if (ndims is < 0 or > 7)
      throw new InvalidDataException($"Invalid NIfTI-2 dimensionality {ndims}.");

    var width64 = ndims >= 1 ? _ReadInt64(data, 24, isBigEndian) : 1;
    var height64 = ndims >= 2 ? _ReadInt64(data, 32, isBigEndian) : 1;
    var depth64 = ndims >= 3 ? _ReadInt64(data, 40, isBigEndian) : 1;
    var width = _ToImageDimension(width64, "width");
    var height = _ToImageDimension(height64, "height");
    var depth = _ToImageDimension(depth64, "depth");

    var pixdim = new float[8];
    for (var i = 0; i < 8; ++i)
      pixdim[i] = checked((float)_ReadDouble(data, 104 + i * 8, isBigEndian));

    var voxOffset = _ReadInt64(data, 168, isBigEndian);
    if (voxOffset < HeaderSize || voxOffset > data.Length)
      throw new InvalidDataException($"Invalid NIfTI-2 vox_offset {voxOffset}.");

    var payload = data[(int)voxOffset..].ToArray();
    if (isBigEndian)
      _NormalizeVoxelEndian(payload, datatype, bitpix);

    return new NiftiFile {
      Width = width,
      Height = height,
      Depth = depth,
      Datatype = datatype,
      Bitpix = bitpix,
      SclSlope = checked((float)_ReadDouble(data, 176, isBigEndian)),
      SclInter = checked((float)_ReadDouble(data, 184, isBigEndian)),
      VoxOffset = 0,
      Description = _ReadAscii(data.Slice(240, 80)),
      PixelData = payload,
      Pixdim = pixdim,
    };
  }

  internal static NiftiFile ParsePair(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload) {
    if (header.Length < HeaderSize)
      throw new InvalidDataException("Data is too small for a NIfTI-2 paired header.");
    var isBigEndian = _IsBigEndian(header);
    if (!header.Slice(4, 8).SequenceEqual(PairMagic))
      throw new InvalidDataException("Invalid NIfTI-2 paired-file magic.");

    var datatype = (NiftiDataType)_ReadInt16(header, 12, isBigEndian);
    var bitpix = _ReadInt16(header, 14, isBigEndian);
    var ndims = _ReadInt64(header, 16, isBigEndian);
    var width = _ToImageDimension(ndims >= 1 ? _ReadInt64(header, 24, isBigEndian) : 1, "width");
    var height = _ToImageDimension(ndims >= 2 ? _ReadInt64(header, 32, isBigEndian) : 1, "height");
    var depth = _ToImageDimension(ndims >= 3 ? _ReadInt64(header, 40, isBigEndian) : 1, "depth");

    var pixdim = new float[8];
    for (var i = 0; i < 8; ++i)
      pixdim[i] = checked((float)_ReadDouble(header, 104 + i * 8, isBigEndian));

    var voxOffset = _ReadInt64(header, 168, isBigEndian);
    if (voxOffset < 0 || voxOffset > payload.Length)
      throw new InvalidDataException($"Invalid NIfTI-2 paired vox_offset {voxOffset}.");
    var pixels = payload[(int)voxOffset..].ToArray();
    if (isBigEndian)
      _NormalizeVoxelEndian(pixels, datatype, bitpix);

    return new NiftiFile {
      Width = width,
      Height = height,
      Depth = depth,
      Datatype = datatype,
      Bitpix = bitpix,
      SclSlope = checked((float)_ReadDouble(header, 176, isBigEndian)),
      SclInter = checked((float)_ReadDouble(header, 184, isBigEndian)),
      VoxOffset = 0,
      Description = _ReadAscii(header.Slice(240, 80)),
      PixelData = pixels,
      Pixdim = pixdim,
    };
  }

  internal static byte[] WriteSingle(NiftiFile file) {
    var header = WriteHeader(file, pair: false);
    var result = new byte[checked(SingleDataOffset + file.PixelData.Length)];
    header.CopyTo(result, 0);
    file.PixelData.AsSpan().CopyTo(result.AsSpan(SingleDataOffset));
    return result;
  }

  internal static byte[] WriteHeader(NiftiFile file, bool pair) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Width < 1 || file.Height < 1 || file.Depth < 1)
      throw new InvalidDataException("NIfTI-2 dimensions must be positive.");

    var result = new byte[HeaderSize];
    BinaryPrimitives.WriteInt32LittleEndian(result, HeaderSize);
    (pair ? PairMagic : SingleMagic).CopyTo(result.AsSpan(4, 8));
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(12), (short)file.Datatype);
    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(14), file.Bitpix);

    var ndims = file.Depth > 1 ? 3L : 2L;
    BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(16), ndims);
    BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(24), file.Width);
    BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(32), file.Height);
    BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(40), file.Depth);
    for (var i = 3; i < 7; ++i)
      BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(16 + (i + 1) * 8), 1);

    for (var i = 0; i < 8; ++i) {
      var value = file.Pixdim.Length > i ? file.Pixdim[i] : 0f;
      _WriteDouble(result, 104 + i * 8, value);
    }

    BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(168), pair ? 0 : SingleDataOffset);
    _WriteDouble(result, 176, file.SclSlope);
    _WriteDouble(result, 184, file.SclInter);
    _WriteAscii(result.AsSpan(240, 80), file.Description);
    return result;
  }

  private static bool _IsBigEndian(ReadOnlySpan<byte> data) {
    var little = BinaryPrimitives.ReadInt32LittleEndian(data);
    if (little == HeaderSize)
      return false;
    if (BinaryPrimitives.ReadInt32BigEndian(data) == HeaderSize)
      return true;
    throw new InvalidDataException("NIfTI-2 sizeof_hdr is neither little- nor big-endian 540.");
  }

  private static short _ReadInt16(ReadOnlySpan<byte> data, int offset, bool big)
    => big ? BinaryPrimitives.ReadInt16BigEndian(data[offset..]) : BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);

  private static long _ReadInt64(ReadOnlySpan<byte> data, int offset, bool big)
    => big ? BinaryPrimitives.ReadInt64BigEndian(data[offset..]) : BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);

  private static double _ReadDouble(ReadOnlySpan<byte> data, int offset, bool big) {
    var bits = big ? BinaryPrimitives.ReadInt64BigEndian(data[offset..]) : BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);
    return BitConverter.Int64BitsToDouble(bits);
  }

  private static void _WriteDouble(Span<byte> data, int offset, double value)
    => BinaryPrimitives.WriteInt64LittleEndian(data[offset..], BitConverter.DoubleToInt64Bits(value));

  private static int _ToImageDimension(long value, string name) {
    if (value < 1 || value > int.MaxValue)
      throw new InvalidDataException($"NIfTI-2 {name} {value} cannot be represented by RawImage.");
    return (int)value;
  }

  private static string _ReadAscii(ReadOnlySpan<byte> bytes) {
    var zero = bytes.IndexOf((byte)0);
    if (zero >= 0)
      bytes = bytes[..zero];
    return Encoding.ASCII.GetString(bytes).TrimEnd();
  }

  private static void _WriteAscii(Span<byte> destination, string? value) {
    destination.Clear();
    if (string.IsNullOrEmpty(value))
      return;
    var count = Encoding.ASCII.GetBytes(value.AsSpan(), destination);
    if (count == destination.Length)
      destination[^1] = 0;
  }

  private static void _NormalizeVoxelEndian(byte[] pixels, NiftiDataType datatype, short bitpix) {
    var bytesPerVoxel = datatype switch {
      NiftiDataType.Rgb24 => 1,
      NiftiDataType.Rgba32 => 1,
      _ => Math.Abs(bitpix) / 8,
    };
    if (bytesPerVoxel <= 1)
      return;
    if (bytesPerVoxel is not 2 and not 4 and not 8)
      throw new InvalidDataException($"Cannot endian-normalize NIfTI datatype {datatype} with BITPIX {bitpix}.");

    for (var offset = 0; offset + bytesPerVoxel <= pixels.Length; offset += bytesPerVoxel)
      Array.Reverse(pixels, offset, bytesPerVoxel);
  }
}
