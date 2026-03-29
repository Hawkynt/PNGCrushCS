using System;
using System.IO;

namespace FileFormat.Dds;

/// <summary>Assembles DDS file bytes from a DdsFile model.</summary>
public static class DdsWriter {

  private const int _MAGIC = 0x20534444; // "DDS " in little-endian
  private const int _MAGIC_SIZE = 4;

  public static byte[] ToBytes(DdsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelFormat = _BuildPixelFormat(file.Format);
    var flags = DdsHeader.DDSD_CAPS | DdsHeader.DDSD_HEIGHT | DdsHeader.DDSD_WIDTH | DdsHeader.DDSD_PIXELFORMAT;

    if (file.MipMapCount > 1)
      flags |= DdsHeader.DDSD_MIPMAPCOUNT;

    if (file.Depth > 1)
      flags |= DdsHeader.DDSD_DEPTH;

    var blockSize = DdsBlockInfo.GetBlockSize(file.Format);
    var pitchOrLinearSize = 0;
    if (blockSize > 0) {
      flags |= DdsHeader.DDSD_LINEARSIZE;
      pitchOrLinearSize = DdsBlockInfo.CalculateMipSize(file.Width, file.Height, file.Format);
    }

    var caps = DdsHeader.DDSCAPS_TEXTURE;
    if (file.MipMapCount > 1)
      caps |= DdsHeader.DDSCAPS_COMPLEX | DdsHeader.DDSCAPS_MIPMAP;

    var header = new DdsHeader(
      DdsHeader.StructSize,
      flags,
      file.Height,
      file.Width,
      pitchOrLinearSize,
      file.Depth > 1 ? file.Depth : 0,
      file.MipMapCount,
      pixelFormat,
      caps,
      0,
      0,
      0,
      0
    );

    using var ms = new MemoryStream();

    // Write magic
    var magicBytes = new byte[_MAGIC_SIZE];
    BitConverter.TryWriteBytes(magicBytes, _MAGIC);
    ms.Write(magicBytes, 0, _MAGIC_SIZE);

    // Write header
    var headerBytes = new byte[DdsHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes, 0, DdsHeader.StructSize);

    // Write DX10 header if needed
    if (file.HasDx10Header) {
      var dx10 = new DdsDx10Header(0, (int)DdsResourceType.Texture2D, 0, 1, 0);
      var dx10Bytes = new byte[DdsDx10Header.StructSize];
      dx10.WriteTo(dx10Bytes);
      ms.Write(dx10Bytes, 0, DdsDx10Header.StructSize);
    }

    // Write surface data
    foreach (var surface in file.Surfaces)
      ms.Write(surface.Data, 0, surface.Data.Length);

    return ms.ToArray();
  }

  private static DdsPixelFormat _BuildPixelFormat(DdsFormat format) => format switch {
    DdsFormat.Dxt1 => new(DdsPixelFormat.StructSize, DdsHeader.DDPF_FOURCC, _MakeFourCC('D', 'X', 'T', '1'), 0, 0, 0, 0, 0),
    DdsFormat.Dxt3 => new(DdsPixelFormat.StructSize, DdsHeader.DDPF_FOURCC, _MakeFourCC('D', 'X', 'T', '3'), 0, 0, 0, 0, 0),
    DdsFormat.Dxt5 => new(DdsPixelFormat.StructSize, DdsHeader.DDPF_FOURCC, _MakeFourCC('D', 'X', 'T', '5'), 0, 0, 0, 0, 0),
    DdsFormat.Dx10 => new(DdsPixelFormat.StructSize, DdsHeader.DDPF_FOURCC, _MakeFourCC('D', 'X', '1', '0'), 0, 0, 0, 0, 0),
    DdsFormat.Rgba => new(DdsPixelFormat.StructSize, DdsHeader.DDPF_RGB | DdsHeader.DDPF_ALPHAPIXELS, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, unchecked((int)0xFF000000)),
    DdsFormat.Rgb => new(DdsPixelFormat.StructSize, DdsHeader.DDPF_RGB, 0, 24, 0x00FF0000, 0x0000FF00, 0x000000FF, 0),
    _ => new(DdsPixelFormat.StructSize, 0, 0, 0, 0, 0, 0, 0)
  };

  private static int _MakeFourCC(char a, char b, char c, char d) =>
    a | (b << 8) | (c << 16) | (d << 24);
}
