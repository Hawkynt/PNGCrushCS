using System;
using System.IO;

namespace FileFormat.Blp;

/// <summary>Assembles BLP2 file bytes from a BlpFile model.</summary>
public static class BlpWriter {

  private const int _HEADER_SIZE = 148;
  private const int _PALETTE_SIZE = 1024;
  private const int _MAX_MIPS = 16;

  public static byte[] ToBytes(BlpFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var hasPalette = file.Encoding == BlpEncoding.Palette && file.Palette != null;
    var dataStart = _HEADER_SIZE + (hasPalette ? _PALETTE_SIZE : 0);

    // Calculate total size
    var totalDataSize = 0;
    foreach (var mip in file.MipData)
      totalDataSize += mip.Length;

    var totalSize = dataStart + totalDataSize;
    var result = new byte[totalSize];
    using var ms = new MemoryStream(result);
    using var bw = new BinaryWriter(ms);

    // Magic "BLP2"
    bw.Write((uint)0x32504C42);

    // Type (0 = DirectX/Uncompressed, 1 = JPEG internal)
    bw.Write(file.Encoding == BlpEncoding.Palette || file.Encoding == BlpEncoding.UncompressedBgra || file.Encoding == BlpEncoding.Dxt ? 0U : 1U);

    // Encoding, AlphaDepth, AlphaEncoding, HasMips
    bw.Write((byte)file.Encoding);
    bw.Write(file.AlphaDepth);
    bw.Write((byte)file.AlphaEncoding);
    bw.Write(file.HasMips ? (byte)1 : (byte)0);

    // Width, Height
    bw.Write((uint)file.Width);
    bw.Write((uint)file.Height);

    // Mip offsets (16 entries)
    var currentOffset = (uint)dataStart;
    for (var i = 0; i < _MAX_MIPS; ++i) {
      if (i < file.MipData.Length && file.MipData[i].Length > 0) {
        bw.Write(currentOffset);
        currentOffset += (uint)file.MipData[i].Length;
      } else
        bw.Write(0U);
    }

    // Mip sizes (16 entries)
    for (var i = 0; i < _MAX_MIPS; ++i) {
      if (i < file.MipData.Length)
        bw.Write((uint)file.MipData[i].Length);
      else
        bw.Write(0U);
    }

    // Palette
    if (hasPalette) {
      var palData = file.Palette!;
      var toWrite = Math.Min(palData.Length, _PALETTE_SIZE);
      bw.Write(palData, 0, toWrite);
      // Pad remainder if palette is shorter than 1024 bytes
      for (var i = toWrite; i < _PALETTE_SIZE; ++i)
        bw.Write((byte)0);
    }

    // Mipmap data
    foreach (var mip in file.MipData)
      bw.Write(mip);

    return result;
  }
}
