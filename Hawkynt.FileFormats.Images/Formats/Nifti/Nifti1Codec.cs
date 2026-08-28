using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Nifti;

/// <summary>Endian-aware NIfTI-1 header projection into the image-oriented <see cref="NiftiFile"/> model.</summary>
internal static class Nifti1Codec {
  internal const int HeaderSize = 348;

  internal static bool? Matches(ReadOnlySpan<byte> data, bool pair) {
    if (data.Length < HeaderSize)
      return null;
    var little = BinaryPrimitives.ReadInt32LittleEndian(data);
    var big = BinaryPrimitives.ReadInt32BigEndian(data);
    if (little != HeaderSize && big != HeaderSize)
      return null;
    var expected = pair ? "ni1\0"u8 : "n+1\0"u8;
    return data.Slice(344, 4).SequenceEqual(expected) ? true : null;
  }

  internal static NiftiFile ParseSingle(ReadOnlySpan<byte> data) {
    if (data.Length < HeaderSize)
      throw new InvalidDataException("Data too small for a valid NIfTI-1 file.");
    var big = IsBigEndian(data);
    if (!data.Slice(344, 4).SequenceEqual("n+1\0"u8))
      throw new InvalidDataException("Invalid NIfTI-1 single-file magic.");

    var header = ParseHeader(data, big);
    var start = checked((int)Math.Max(HeaderSize, header.VoxOffset));
    if (start > data.Length)
      throw new InvalidDataException("NIfTI vox_offset exceeds file length.");
    var pixels = data[start..].ToArray();
    if (big)
      NormalizeVoxelEndian(pixels, header.Datatype, header.Bitpix);
    return WithPixels(header, pixels);
  }

  internal static NiftiFile ParsePair(ReadOnlySpan<byte> headerBytes, ReadOnlySpan<byte> payload) {
    if (headerBytes.Length < HeaderSize)
      throw new InvalidDataException("Data too small for a valid NIfTI-1 paired header.");
    var big = IsBigEndian(headerBytes);
    if (!headerBytes.Slice(344, 4).SequenceEqual("ni1\0"u8))
      throw new InvalidDataException("Invalid NIfTI-1 paired-file magic.");

    var header = ParseHeader(headerBytes, big);
    var start = checked((int)Math.Max(0, header.VoxOffset));
    if (start > payload.Length)
      throw new InvalidDataException("NIfTI vox_offset exceeds .img companion length.");
    var pixels = payload[start..].ToArray();
    if (big)
      NormalizeVoxelEndian(pixels, header.Datatype, header.Bitpix);
    return WithPixels(header, pixels);
  }

  private static NiftiFile ParseHeader(ReadOnlySpan<byte> data, bool big) {
    var ndims = ReadInt16(data, 40, big);
    if (ndims is < 0 or > 7)
      throw new InvalidDataException($"Invalid NIfTI-1 dimensionality {ndims}.");
    var width = ndims >= 1 ? ReadInt16(data, 42, big) : (short)1;
    var height = ndims >= 2 ? ReadInt16(data, 44, big) : (short)1;
    var depth = ndims >= 3 ? ReadInt16(data, 46, big) : (short)1;
    if (width < 1 || height < 1 || depth < 1)
      throw new InvalidDataException("NIfTI-1 contains a non-positive image dimension.");

    var pixdim = new float[8];
    for (var i = 0; i < 8; ++i)
      pixdim[i] = ReadSingle(data, 76 + i * 4, big);

    return new NiftiFile {
      Width = width,
      Height = height,
      Depth = depth,
      Datatype = (NiftiDataType)ReadInt16(data, 70, big),
      Bitpix = ReadInt16(data, 72, big),
      SclSlope = ReadSingle(data, 112, big),
      SclInter = ReadSingle(data, 116, big),
      VoxOffset = ReadSingle(data, 108, big),
      Description = ReadAscii(data.Slice(148, 80)),
      PixelData = [],
      Pixdim = pixdim,
    };
  }

  private static NiftiFile WithPixels(NiftiFile header, byte[] pixels) => new() {
    Width = header.Width,
    Height = header.Height,
    Depth = header.Depth,
    Datatype = header.Datatype,
    Bitpix = header.Bitpix,
    SclSlope = header.SclSlope,
    SclInter = header.SclInter,
    VoxOffset = header.VoxOffset,
    Description = header.Description,
    PixelData = pixels,
    Pixdim = header.Pixdim,
  };

  private static bool IsBigEndian(ReadOnlySpan<byte> data) {
    if (BinaryPrimitives.ReadInt32LittleEndian(data) == HeaderSize)
      return false;
    if (BinaryPrimitives.ReadInt32BigEndian(data) == HeaderSize)
      return true;
    throw new InvalidDataException("NIfTI-1 sizeof_hdr is neither little- nor big-endian 348.");
  }

  private static short ReadInt16(ReadOnlySpan<byte> data, int offset, bool big)
    => big ? BinaryPrimitives.ReadInt16BigEndian(data[offset..]) : BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);

  private static float ReadSingle(ReadOnlySpan<byte> data, int offset, bool big) {
    var bits = big ? BinaryPrimitives.ReadInt32BigEndian(data[offset..]) : BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    return BitConverter.Int32BitsToSingle(bits);
  }

  private static string ReadAscii(ReadOnlySpan<byte> bytes) {
    var zero = bytes.IndexOf((byte)0);
    if (zero >= 0)
      bytes = bytes[..zero];
    return Encoding.ASCII.GetString(bytes).TrimEnd();
  }

  internal static void NormalizeVoxelEndian(byte[] pixels, NiftiDataType datatype, short bitpix) {
    var bytesPerVoxel = datatype switch {
      NiftiDataType.Rgb24 or NiftiDataType.Rgba32 => 1,
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
