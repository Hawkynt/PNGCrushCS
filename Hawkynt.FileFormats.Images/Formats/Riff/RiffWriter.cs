using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Riff;

/// <summary>Assembles RIFF container files.</summary>
public static class RiffWriter {

  public static byte[] ToBytes(RiffFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();
    Span<byte> header = stackalloc byte[RiffHeader.StructSize];
    new RiffHeader("RIFF", 0, file.FormType).WriteTo(header);
    ms.Write(header);

    _WriteElements(ms, file.Chunks, file.Lists);

    // Patch file size (total - 8 bytes for RIFF+Size fields)
    var totalSize = (uint)(ms.Length - 8);
    ms.Position = 4;
    Span<byte> sizeBytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, totalSize);
    ms.Write(sizeBytes);

    return ms.ToArray();
  }

  private static void _WriteElements(MemoryStream ms, System.Collections.Generic.List<RiffChunk> chunks, System.Collections.Generic.List<RiffList> lists) {
    foreach (var list in lists)
      _WriteList(ms, list);

    foreach (var chunk in chunks)
      _WriteChunk(ms, chunk);
  }

  private static void _WriteChunk(MemoryStream ms, RiffChunk chunk) {
    Span<byte> header = stackalloc byte[RiffChunkHeader.StructSize];
    new RiffChunkHeader(chunk.Id, (uint)chunk.Data.Length).WriteTo(header);
    ms.Write(header);
    ms.Write(chunk.Data);

    // Word-align
    if ((chunk.Data.Length & 1) != 0)
      ms.WriteByte(0);
  }

  private static void _WriteList(MemoryStream ms, RiffList list) {
    var startPos = ms.Position;
    Span<byte> header = stackalloc byte[RiffHeader.StructSize];
    new RiffHeader("LIST", 0, list.ListType).WriteTo(header);
    ms.Write(header);

    _WriteElements(ms, list.Chunks, list.SubLists);

    // Patch list size
    var listSize = (uint)(ms.Position - startPos - 8);
    var currentPos = ms.Position;
    ms.Position = startPos + 4;
    Span<byte> sizeBytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, listSize);
    ms.Write(sizeBytes);
    ms.Position = currentPos;

    // Word-align
    if ((listSize & 1) != 0)
      ms.WriteByte(0);
  }
}
