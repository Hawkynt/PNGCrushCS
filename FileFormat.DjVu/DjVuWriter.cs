using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.DjVu.Codec;

namespace FileFormat.DjVu;

/// <summary>Assembles DjVu file bytes from an in-memory representation.</summary>
public static class DjVuWriter {

  /// <summary>Custom marker bytes at the start of PM44 data to identify our raw pixel format.</summary>
  private static ReadOnlySpan<byte> _RawPixelMarker => "RAWP"u8;

  private const int _INFO_SIZE = 10;

  public static byte[] ToBytes(DjVuFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Write AT&T magic
    ms.Write("AT&T"u8);

    // Write FORM tag
    ms.Write("FORM"u8);

    // Placeholder for FORM size (will be filled after assembling all chunks)
    var formSizeOffset = (int)ms.Position;
    ms.Write(stackalloc byte[4]);

    // Write DJVU form type
    ms.Write("DJVU"u8);

    // Write INFO chunk
    _WriteChunk(ms, "INFO", _BuildInfoData(file));

    // Write preserved raw chunks
    foreach (var chunk in file.RawChunks)
      _WriteChunk(ms, chunk.ChunkId, chunk.Data);

    // Write BG44 chunk with IW44 wavelet-encoded pixel data
    if (file.PixelData.Length > 0) {
      try {
        var iw44Data = Iw44Encoder.Encode(file.PixelData, file.Width, file.Height);
        _WriteChunk(ms, "BG44", iw44Data);
      } catch {
        // Fall back to raw pixel marker if encoding fails
        var pm44Data = new byte[4 + file.PixelData.Length];
        _RawPixelMarker.CopyTo(pm44Data);
        file.PixelData.AsSpan(0, file.PixelData.Length).CopyTo(pm44Data.AsSpan(4));
        _WriteChunk(ms, DjVuReader.PM44_CHUNK_ID, pm44Data);
      }
    }

    // Patch FORM size: total bytes after the FORM size field, excluding AT&T magic and FORM tag and size field itself
    var totalSize = (int)ms.Position;
    var formSize = totalSize - 12; // everything after 4(AT&T) + 4(FORM) + 4(size)
    var result = ms.ToArray();
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(formSizeOffset), (uint)formSize);

    return result;
  }

  private static byte[] _BuildInfoData(DjVuFile file) {
    var info = new byte[_INFO_SIZE];
    var span = info.AsSpan();
    BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], (ushort)file.Height);
    span[4] = file.VersionMinor;
    span[5] = file.VersionMajor;
    BinaryPrimitives.WriteUInt16LittleEndian(span[6..], (ushort)file.Dpi);
    span[8] = file.Gamma;
    span[9] = file.Flags;
    return info;
  }

  private static void _WriteChunk(MemoryStream ms, string chunkId, byte[] data) {
    // 4-byte chunk ID
    Span<byte> idBytes = stackalloc byte[4];
    Encoding.ASCII.GetBytes(chunkId.AsSpan(0, Math.Min(chunkId.Length, 4)), idBytes);
    ms.Write(idBytes);

    // 4-byte big-endian size
    Span<byte> sizeBytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(sizeBytes, (uint)data.Length);
    ms.Write(sizeBytes);

    // Chunk data
    ms.Write(data);

    // Word-align (2-byte boundary)
    if ((data.Length & 1) != 0)
      ms.WriteByte(0);
  }
}
