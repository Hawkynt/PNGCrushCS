using System;
using System.IO;

namespace FileFormat.Fli;

/// <summary>Assembles FLI/FLC animation file bytes from a <see cref="FliFile"/>.</summary>
public static class FliWriter {

  private const short _FRAME_MAGIC = unchecked((short)0xF1FA);

  public static byte[] ToBytes(FliFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // Reserve space for file header (128 bytes)
    ms.Write(new byte[FliHeader.StructSize]);

    var chunkHeaderBuffer = new byte[FliChunkHeader.StructSize];
    var frameHeaderBuffer = new byte[FliFrameHeader.StructSize];

    // Write frames
    foreach (var frame in file.Frames) {
      var frameStart = ms.Position;

      // Reserve space for frame header (16 bytes)
      ms.Write(new byte[FliFrameHeader.StructSize]);

      // Write chunks
      foreach (var chunk in frame.Chunks) {
        var chunkSize = FliChunkHeader.StructSize + chunk.Data.Length;
        var chunkHeader = new FliChunkHeader(chunkSize, (short)chunk.ChunkType);
        chunkHeader.WriteTo(chunkHeaderBuffer);
        ms.Write(chunkHeaderBuffer);
        ms.Write(chunk.Data);
      }

      // Patch frame header
      var frameEnd = ms.Position;
      var frameSize = (int)(frameEnd - frameStart);
      ms.Position = frameStart;

      var frameHeader = new FliFrameHeader(frameSize, _FRAME_MAGIC, (short)frame.Chunks.Count);
      frameHeader.WriteTo(frameHeaderBuffer);
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
