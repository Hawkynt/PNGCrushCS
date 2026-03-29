using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Dds;

/// <summary>Reads DDS files from bytes, streams, or file paths.</summary>
public static class DdsReader {

  private const int _MAGIC = 0x20534444; // "DDS " in little-endian
  private const int _MAGIC_SIZE = 4;
  private const int _MIN_FILE_SIZE = _MAGIC_SIZE + DdsHeader.StructSize; // 128

  private static readonly int _FOURCC_DXT1 = _MakeFourCC('D', 'X', 'T', '1');
  private static readonly int _FOURCC_DXT3 = _MakeFourCC('D', 'X', 'T', '3');
  private static readonly int _FOURCC_DXT5 = _MakeFourCC('D', 'X', 'T', '5');
  private static readonly int _FOURCC_DX10 = _MakeFourCC('D', 'X', '1', '0');
  private static readonly int _FOURCC_ATI1 = _MakeFourCC('A', 'T', 'I', '1');
  private static readonly int _FOURCC_BC4U = _MakeFourCC('B', 'C', '4', 'U');
  private static readonly int _FOURCC_ATI2 = _MakeFourCC('A', 'T', 'I', '2');
  private static readonly int _FOURCC_BC5U = _MakeFourCC('B', 'C', '5', 'U');

  public static DdsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DDS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DdsFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static DdsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_FILE_SIZE)
      throw new InvalidDataException("Data too small for a valid DDS file.");

    var span = data.AsSpan();

    var magic = BinaryPrimitives.ReadInt32LittleEndian(span);
    if (magic != _MAGIC)
      throw new InvalidDataException("Invalid DDS magic number.");

    var header = DdsHeader.ReadFrom(span[_MAGIC_SIZE..]);
    if (header.Size != DdsHeader.StructSize)
      throw new InvalidDataException($"Invalid DDS header size: {header.Size}.");

    var format = _DetectFormat(header.PixelFormat);
    var hasDx10 = false;
    var dataOffset = _MAGIC_SIZE + DdsHeader.StructSize;

    if (format == DdsFormat.Dx10) {
      if (data.Length < dataOffset + DdsDx10Header.StructSize)
        throw new InvalidDataException("Data too small for DX10 extended header.");

      hasDx10 = true;
      var dx10 = DdsDx10Header.ReadFrom(span.Slice(dataOffset));
      format = _MapDxgiFormat(dx10.DxgiFormat) ?? format;
      dataOffset += DdsDx10Header.StructSize;
    }

    var width = header.Width;
    var height = header.Height;
    var depth = header.Depth > 0 ? header.Depth : 1;
    var mipMapCount = header.MipMapCount > 0 ? header.MipMapCount : 1;

    var surfaces = new List<DdsSurface>();
    var offset = dataOffset;

    for (var mip = 0; mip < mipMapCount; ++mip) {
      var mipWidth = Math.Max(1, width >> mip);
      var mipHeight = Math.Max(1, height >> mip);
      var mipSize = DdsBlockInfo.CalculateMipSize(mipWidth, mipHeight, format);

      var surfaceData = new byte[mipSize];
      var available = Math.Min(mipSize, data.Length - offset);
      if (available > 0)
        data.AsSpan(offset, available).CopyTo(surfaceData.AsSpan(0));

      surfaces.Add(new DdsSurface {
        Width = mipWidth,
        Height = mipHeight,
        MipLevel = mip,
        Data = surfaceData
      });

      offset += mipSize;
    }

    return new DdsFile {
      Width = width,
      Height = height,
      Depth = depth,
      MipMapCount = mipMapCount,
      Format = format,
      HasDx10Header = hasDx10,
      Surfaces = surfaces
    };
  }

  private static DdsFormat _DetectFormat(DdsPixelFormat pf) {
    if ((pf.Flags & DdsHeader.DDPF_FOURCC) != 0) {
      if (pf.FourCC == _FOURCC_DXT1)
        return DdsFormat.Dxt1;
      if (pf.FourCC == _FOURCC_DXT3)
        return DdsFormat.Dxt3;
      if (pf.FourCC == _FOURCC_DXT5)
        return DdsFormat.Dxt5;
      if (pf.FourCC == _FOURCC_DX10)
        return DdsFormat.Dx10;
      if (pf.FourCC == _FOURCC_ATI1 || pf.FourCC == _FOURCC_BC4U)
        return DdsFormat.Bc4;
      if (pf.FourCC == _FOURCC_ATI2 || pf.FourCC == _FOURCC_BC5U)
        return DdsFormat.Bc5;

      return DdsFormat.Unknown;
    }

    if ((pf.Flags & DdsHeader.DDPF_RGB) != 0) {
      if ((pf.Flags & DdsHeader.DDPF_ALPHAPIXELS) != 0 && pf.RGBBitCount == 32)
        return DdsFormat.Rgba;

      if (pf.RGBBitCount == 24)
        return DdsFormat.Rgb;
    }

    return DdsFormat.Unknown;
  }

  private static DdsFormat? _MapDxgiFormat(int dxgiFormat) => dxgiFormat switch {
    // DXGI_FORMAT_BC1_UNORM / SRGB
    70 or 71 => DdsFormat.Dxt1,
    // DXGI_FORMAT_BC2_UNORM / SRGB
    73 or 74 => DdsFormat.Dxt3,
    // DXGI_FORMAT_BC3_UNORM / SRGB
    76 or 77 => DdsFormat.Dxt5,
    // DXGI_FORMAT_BC4_UNORM / SNORM
    79 or 80 => DdsFormat.Bc4,
    // DXGI_FORMAT_BC5_UNORM / SNORM
    82 or 83 => DdsFormat.Bc5,
    // DXGI_FORMAT_BC6H_UF16
    95 => DdsFormat.Bc6HUnsigned,
    // DXGI_FORMAT_BC6H_SF16
    96 => DdsFormat.Bc6HSigned,
    // DXGI_FORMAT_BC7_UNORM / SRGB
    97 or 98 => DdsFormat.Bc7,
    // DXGI_FORMAT_R8G8B8A8_UNORM
    28 => DdsFormat.Rgba,
    _ => null
  };

  private static int _MakeFourCC(char a, char b, char c, char d) =>
    a | (b << 8) | (c << 16) | (d << 24);
}
