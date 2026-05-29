using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Riff;

namespace FileFormat.Iff;

/// <summary>Assembles IFF (Interchange File Format) container files.</summary>
public static class IffWriter {

  private static readonly System.Collections.Generic.HashSet<string> _GroupIds = ["FORM", "LIST", "CAT "];

  public static byte[] ToBytes(IffFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();
    Span<byte> header = stackalloc byte[IffChunkHeader.StructSize];
    new IffChunkHeader("FORM", 0).WriteTo(header);
    ms.Write(header);

    // Write form type
    Span<byte> formType = stackalloc byte[4];
    file.FormType.WriteTo(formType);
    ms.Write(formType);

    _WriteChunks(ms, file.Chunks);

    // Patch outer size (total - 8 bytes for ChunkId+Size fields)
    var totalSize = (int)(ms.Length - IffChunkHeader.StructSize);
    ms.Position = 4;
    Span<byte> sizeBytes = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(sizeBytes, totalSize);
    ms.Write(sizeBytes);

    return ms.ToArray();
  }

  private static void _WriteChunks(MemoryStream ms, System.Collections.Generic.IReadOnlyList<IffChunk> chunks) {
    foreach (var chunk in chunks)
      _WriteChunk(ms, chunk);
  }

  private static void _WriteChunk(MemoryStream ms, IffChunk chunk) {
    var chunkId = chunk.ChunkId.ToString();

    if (_GroupIds.Contains(chunkId) && chunk.SubChunks is { } subChunks) {
      var startPos = ms.Position;

      // Write group header: ChunkId + placeholder size + form/list type from first 4 bytes of Data
      Span<byte> header = stackalloc byte[IffChunkHeader.StructSize];
      new IffChunkHeader(chunk.ChunkId, 0).WriteTo(header);
      ms.Write(header);

      // Write the sub-form type (first 4 bytes of Data)
      ms.Write(chunk.Data, 0, 4);

      _WriteChunks(ms, subChunks);

      // Patch group size
      var groupSize = (int)(ms.Position - startPos - IffChunkHeader.StructSize);
      var currentPos = ms.Position;
      ms.Position = startPos + 4;
      Span<byte> sizeBytes = stackalloc byte[4];
      BinaryPrimitives.WriteInt32BigEndian(sizeBytes, groupSize);
      ms.Write(sizeBytes);
      ms.Position = currentPos;

      // Word-align
      if ((groupSize & 1) != 0)
        ms.WriteByte(0);
    } else {
      Span<byte> header = stackalloc byte[IffChunkHeader.StructSize];
      new IffChunkHeader(chunk.ChunkId, chunk.Data.Length).WriteTo(header);
      ms.Write(header);
      ms.Write(chunk.Data);

      // Word-align
      if ((chunk.Data.Length & 1) != 0)
        ms.WriteByte(0);
    }
  }
}
