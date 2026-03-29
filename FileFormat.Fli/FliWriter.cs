using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Fli;

/// <summary>Assembles FLI/FLC animation file bytes from a <see cref="FliFile"/>.</summary>
public static class FliWriter {

  private const short _FRAME_MAGIC = unchecked((short)0xF1FA);
  private const int _FRAME_HEADER_SIZE = 16;
  private const int _CHUNK_HEADER_SIZE = 6;

  public static byte[] ToBytes(FliFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Reserve space for file header (128 bytes)
    ms.Write(new byte[FliHeader.StructSize]);

    var chunkHeaderBuffer = new byte[_CHUNK_HEADER_SIZE];
    var frameHeaderBuffer = new byte[_FRAME_HEADER_SIZE];

    // Write frames
    foreach (var frame in file.Frames) {
      var frameStart = ms.Position;

      // Reserve space for frame header (16 bytes)
      ms.Write(new byte[_FRAME_HEADER_SIZE]);

      // Write chunks
      foreach (var chunk in frame.Chunks) {
        var chunkSize = _CHUNK_HEADER_SIZE + chunk.Data.Length;
        BinaryPrimitives.WriteInt32LittleEndian(chunkHeaderBuffer, chunkSize);
        BinaryPrimitives.WriteInt16LittleEndian(chunkHeaderBuffer.AsSpan(4), (short)chunk.ChunkType);
        ms.Write(chunkHeaderBuffer);
        ms.Write(chunk.Data);
      }

      // Patch frame header
      var frameEnd = ms.Position;
      var frameSize = (int)(frameEnd - frameStart);
      ms.Position = frameStart;

      Array.Clear(frameHeaderBuffer);
      BinaryPrimitives.WriteInt32LittleEndian(frameHeaderBuffer, frameSize);
      BinaryPrimitives.WriteInt16LittleEndian(frameHeaderBuffer.AsSpan(4), _FRAME_MAGIC);
      BinaryPrimitives.WriteInt16LittleEndian(frameHeaderBuffer.AsSpan(6), (short)frame.Chunks.Count);
      ms.Write(frameHeaderBuffer);

      ms.Position = frameEnd;
    }

    // Patch file header
    var totalSize = (int)ms.Length;
    ms.Position = 0;

    var header = new FliHeader(
      totalSize,
      (short)file.FrameType,
      file.FrameCount,
      file.Width,
      file.Height,
      8,
      0,
      file.Speed
    );

    var headerBytes = new byte[FliHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes);

    return ms.ToArray();
  }
}
