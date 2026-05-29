using System;
using System.Buffers.Binary;

namespace FileFormat.SymbianMbm;

/// <summary>Assembles Symbian OS MBM file bytes from in-memory representation.</summary>
public static class SymbianMbmWriter {

  public static byte[] ToBytes(SymbianMbmFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file);
  }

  internal static byte[] Assemble(SymbianMbmFile file) {
    var bitmaps = file.Bitmaps;
    var bitmapCount = bitmaps.Length;

    // Calculate total size
    // Header: 20 bytes
    // Each bitmap: headerLen (40) + pixel data
    // Trailer: 4 (count) + 4 * bitmapCount (offsets)
    var bitmapSizes = new int[bitmapCount];
    var totalBitmapBytes = 0;
    for (var i = 0; i < bitmapCount; ++i) {
      bitmapSizes[i] = SymbianMbmFile.BitmapHeaderSize + bitmaps[i].PixelData.Length;
      totalBitmapBytes += bitmapSizes[i];
    }

    var trailerSize = 4 + bitmapCount * 4;
    var totalSize = SymbianMbmFile.HeaderSize + totalBitmapBytes + trailerSize;
    var result = new byte[totalSize];
    var span = result.AsSpan();

    // Write file header
    var trailerOffset = SymbianMbmFile.HeaderSize + totalBitmapBytes;
    var fileHeader = new SymbianMbmFileHeader(
      SymbianMbmFile.Uid1, SymbianMbmFile.Uid2, SymbianMbmFile.Uid3,
      _ComputeChecksum(SymbianMbmFile.Uid1, SymbianMbmFile.Uid2, SymbianMbmFile.Uid3),
      trailerOffset
    );
    fileHeader.WriteTo(span);

    // Write bitmap data
    var currentOffset = SymbianMbmFile.HeaderSize;
    var offsets = new int[bitmapCount];
    for (var i = 0; i < bitmapCount; ++i) {
      offsets[i] = currentOffset;
      var bmp = bitmaps[i];
      var bmpSpan = span[currentOffset..];
      var bitmapTotalSize = SymbianMbmFile.BitmapHeaderSize + bmp.PixelData.Length;

      var bmpHeader = new SymbianMbmBitmapHeader(
        bitmapTotalSize,
        SymbianMbmFile.BitmapHeaderSize,
        bmp.Width, bmp.Height, bmp.BitsPerPixel,
        bmp.ColorMode, bmp.Compression, bmp.PaletteSize, bmp.DataSize, 0
      );
      bmpHeader.WriteTo(bmpSpan);

      bmp.PixelData.AsSpan(0, bmp.PixelData.Length).CopyTo(result.AsSpan(currentOffset + SymbianMbmFile.BitmapHeaderSize));

      currentOffset += bitmapTotalSize;
    }

    // Write trailer
    var trailerSpan = span[trailerOffset..];
    BinaryPrimitives.WriteUInt32LittleEndian(trailerSpan, (uint)bitmapCount);
    for (var i = 0; i < bitmapCount; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(trailerSpan[(4 + i * 4)..], (uint)offsets[i]);

    return result;
  }

  /// <summary>Computes a simple checksum over the three UIDs (Symbian UID checksum algorithm).</summary>
  private static uint _ComputeChecksum(uint uid1, uint uid2, uint uid3) {
    // Symbian uses a CRC-like checksum over the 12 UID bytes.
    // Simplified: XOR-based checksum for compatibility.
    var bytes = new byte[12];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(), uid1);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), uid2);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), uid3);

    // Symbian UID checksum: sum pairs of bytes into nibble-based CRC
    var even = (uint)0;
    var odd = (uint)0;
    for (var i = 0; i < 12; i += 2) {
      even += bytes[i];
      odd += bytes[i + 1];
    }

    return (even & 0xFFFF) | ((odd & 0xFFFF) << 16);
  }
}
