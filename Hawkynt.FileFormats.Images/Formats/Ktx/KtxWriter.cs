using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Ktx;

/// <summary>Assembles KTX file bytes from a KtxFile model.</summary>
public static class KtxWriter {

  public static byte[] ToBytes(KtxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    return file.Version == KtxVersion.Ktx2 ? _WriteKtx2(file) : _WriteKtx1(file);
  }

  private static byte[] _WriteKtx1(KtxFile file) {
    using var ms = new MemoryStream();

    // Build key-value data
    var kvBytes = _BuildKeyValueData(file, bigEndian: false);

    var header = new KtxHeader(
      KtxHeader.EndiannessLE,
      file.GlType,
      file.GlTypeSize,
      file.GlFormat,
      file.GlInternalFormat,
      file.GlBaseInternalFormat,
      file.Width,
      file.Height,
      file.Depth,
      file.ArrayElements,
      file.Faces,
      file.MipmapCount,
      kvBytes.Length
    );

    var headerBytes = new byte[KtxHeader.StructSize];
    header.WriteTo(headerBytes);
    KtxHeader.Identifier.AsSpan().CopyTo(headerBytes);
    ms.Write(headerBytes);

    // Write key-value data
    if (kvBytes.Length > 0)
      ms.Write(kvBytes);

    // Write mip levels
    foreach (var mip in file.MipLevels) {
      var sizeBytes = new byte[4];
      BinaryPrimitives.WriteInt32LittleEndian(sizeBytes, mip.Data.Length);
      ms.Write(sizeBytes);
      ms.Write(mip.Data);

      // Align to 4 bytes
      var padding = (4 - (int)(ms.Position % 4)) % 4;
      for (var i = 0; i < padding; ++i)
        ms.WriteByte(0);
    }

    return ms.ToArray();
  }

  private static byte[] _WriteKtx2(KtxFile file) {
    // Compute layout
    var levelCount = Math.Max(1, file.MipmapCount);
    var levelIndexSize = levelCount * 24; // 3 x int64 per level

    // Build key-value data
    var kvBytes = _BuildKeyValueData(file, bigEndian: false);

    // DFD placeholder (minimal: 4-byte total size = 0)
    var dfdBytes = new byte[4];

    // Compute offsets
    var dfdOffset = Ktx2Header.StructSize + levelIndexSize;
    var kvdOffset = dfdOffset + dfdBytes.Length;
    var dataStart = kvdOffset + kvBytes.Length;
    // Align data start to 4 bytes
    dataStart = (dataStart + 3) & ~3;

    // Compute level offsets
    var levelOffsets = new long[levelCount];
    var levelSizes = new long[levelCount];
    var currentOffset = (long)dataStart;
    for (var i = 0; i < levelCount && i < file.MipLevels.Count; ++i) {
      levelOffsets[i] = currentOffset;
      levelSizes[i] = file.MipLevels[i].Data.Length;
      currentOffset += levelSizes[i];
      // Align to 4 bytes
      currentOffset = (currentOffset + 3) & ~3;
    }

    var header = new Ktx2Header(
      file.VkFormat,
      file.TypeSize,
      file.Width,
      file.Height,
      file.Depth,
      file.ArrayElements,
      file.Faces,
      file.MipmapCount,
      file.SupercompressionScheme,
      dfdOffset,
      dfdBytes.Length,
      kvBytes.Length > 0 ? kvdOffset : 0,
      kvBytes.Length,
      0,
      0
    );

    using var ms = new MemoryStream();

    var headerBytes = new byte[Ktx2Header.StructSize];
    header.WriteTo(headerBytes);
    Ktx2Header.Identifier.AsSpan().CopyTo(headerBytes);
    ms.Write(headerBytes);

    // Write level index
    for (var i = 0; i < levelCount; ++i) {
      var entry = new byte[24];
      BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(0), i < file.MipLevels.Count ? levelOffsets[i] : 0);
      BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(8), i < file.MipLevels.Count ? levelSizes[i] : 0);
      BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(16), i < file.MipLevels.Count ? levelSizes[i] : 0);
      ms.Write(entry);
    }

    // Write DFD
    ms.Write(dfdBytes);

    // Write key-value data
    if (kvBytes.Length > 0)
      ms.Write(kvBytes);

    // Pad to data start
    while (ms.Position < dataStart)
      ms.WriteByte(0);

    // Write mip level data
    foreach (var mip in file.MipLevels) {
      ms.Write(mip.Data);
      var padding = (4 - (int)(ms.Position % 4)) % 4;
      for (var i = 0; i < padding; ++i)
        ms.WriteByte(0);
    }

    return ms.ToArray();
  }

  private static byte[] _BuildKeyValueData(KtxFile file, bool bigEndian) {
    if (file.KeyValues == null || file.KeyValues.Count == 0)
      return [];

    using var ms = new MemoryStream();
    foreach (var kv in file.KeyValues) {
      var keyBytes = Encoding.UTF8.GetBytes(kv.Key);
      var keyAndValueByteSize = keyBytes.Length + 1 + kv.Value.Length; // key + NUL + value

      var sizeBytes = new byte[4];
      if (bigEndian)
        BinaryPrimitives.WriteInt32BigEndian(sizeBytes, keyAndValueByteSize);
      else
        BinaryPrimitives.WriteInt32LittleEndian(sizeBytes, keyAndValueByteSize);

      ms.Write(sizeBytes);
      ms.Write(keyBytes);
      ms.WriteByte(0); // NUL separator
      ms.Write(kv.Value);

      // Align to 4 bytes
      var padding = (4 - (int)(ms.Position % 4)) % 4;
      for (var i = 0; i < padding; ++i)
        ms.WriteByte(0);
    }

    return ms.ToArray();
  }
}
