using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Fli;

/// <summary>Reads FLI/FLC animation files from bytes, streams, or file paths.</summary>
public static class FliReader {

  private const short _FLI_MAGIC = unchecked((short)0xAF11);
  private const short _FLC_MAGIC = unchecked((short)0xAF12);
  private const short _FRAME_MAGIC = unchecked((short)0xF1FA);
  private const int _FRAME_HEADER_SIZE = 16;

  public static FliFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliFile FromStream(Stream stream) {
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

  public static FliFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FliHeader.StructSize)
      throw new InvalidDataException("Data is too small to be a valid FLI/FLC file.");

    var span = data.AsSpan();
    var header = FliHeader.ReadFrom(span);

    if (header.Magic != _FLI_MAGIC && header.Magic != _FLC_MAGIC)
      throw new InvalidDataException($"Invalid FLI/FLC magic: 0x{(ushort)header.Magic:X4}.");

    var frameType = header.Magic == _FLI_MAGIC ? FliFrameType.Fli : FliFrameType.Flc;

    var offset = FliHeader.StructSize;
    var frames = new List<FliFrame>();
    byte[]? palette = null;

    for (var f = 0; f < header.Frames && offset + _FRAME_HEADER_SIZE <= data.Length; ++f) {
      var frameSize = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
      var frameMagic = BinaryPrimitives.ReadInt16LittleEndian(span[(offset + 4)..]);
      var chunkCount = BinaryPrimitives.ReadInt16LittleEndian(span[(offset + 6)..]);

      if (frameMagic != _FRAME_MAGIC)
        throw new InvalidDataException($"Invalid frame magic at offset {offset}: 0x{(ushort)frameMagic:X4}.");

      var chunks = new List<FliFrameChunk>();
      var chunkOffset = offset + _FRAME_HEADER_SIZE;

      for (var c = 0; c < chunkCount && chunkOffset + 6 <= data.Length; ++c) {
        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(span[chunkOffset..]);
        var chunkType = (FliChunkType)BinaryPrimitives.ReadInt16LittleEndian(span[(chunkOffset + 4)..]);

        var dataSize = Math.Max(0, chunkSize - 6);
        var chunkData = new byte[dataSize];
        if (dataSize > 0 && chunkOffset + 6 + dataSize <= data.Length)
          data.AsSpan(chunkOffset + 6, dataSize).CopyTo(chunkData.AsSpan(0));

        chunks.Add(new FliFrameChunk { ChunkType = chunkType, Data = chunkData });

        // Extract palette from Color256 or Color64 chunks
        if (chunkType == FliChunkType.Color256)
          palette = _ParseColor256(chunkData);
        else if (chunkType == FliChunkType.Color64)
          palette = _ParseColor64(chunkData);

        chunkOffset += Math.Max(6, chunkSize);
      }

      frames.Add(new FliFrame { Chunks = chunks });
      offset += Math.Max(_FRAME_HEADER_SIZE, frameSize);
    }

    return new FliFile {
      Width = header.Width,
      Height = header.Height,
      FrameCount = header.Frames,
      Speed = header.Speed,
      FrameType = frameType,
      Palette = palette,
      Frames = frames
    };
  }

  private static byte[] _ParseColor256(byte[] data) {
    var palette = new byte[768]; // 256 * 3
    var inIdx = 0;
    var colorIdx = 0;

    if (data.Length < 2)
      return palette;

    var packetCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(inIdx));
    inIdx += 2;

    for (var p = 0; p < packetCount && inIdx < data.Length && colorIdx < 256; ++p) {
      if (inIdx >= data.Length)
        break;

      var skip = data[inIdx++];
      colorIdx += skip;

      if (inIdx >= data.Length)
        break;

      int count = data[inIdx++];
      if (count == 0)
        count = 256;

      for (var c = 0; c < count && colorIdx < 256 && inIdx + 2 < data.Length; ++c) {
        palette[colorIdx * 3] = data[inIdx++];
        palette[colorIdx * 3 + 1] = data[inIdx++];
        palette[colorIdx * 3 + 2] = data[inIdx++];
        ++colorIdx;
      }
    }

    return palette;
  }

  private static byte[] _ParseColor64(byte[] data) {
    var palette = new byte[768]; // 256 * 3
    var inIdx = 0;
    var colorIdx = 0;

    if (data.Length < 2)
      return palette;

    var packetCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(inIdx));
    inIdx += 2;

    for (var p = 0; p < packetCount && inIdx < data.Length && colorIdx < 256; ++p) {
      if (inIdx >= data.Length)
        break;

      var skip = data[inIdx++];
      colorIdx += skip;

      if (inIdx >= data.Length)
        break;

      int count = data[inIdx++];
      if (count == 0)
        count = 256;

      for (var c = 0; c < count && colorIdx < 256 && inIdx + 2 < data.Length; ++c) {
        palette[colorIdx * 3] = (byte)(data[inIdx++] << 2);     // multiply by 4
        palette[colorIdx * 3 + 1] = (byte)(data[inIdx++] << 2);
        palette[colorIdx * 3 + 2] = (byte)(data[inIdx++] << 2);
        ++colorIdx;
      }
    }

    return palette;
  }
}
