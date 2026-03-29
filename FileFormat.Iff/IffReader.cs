using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Riff;

namespace FileFormat.Iff;

/// <summary>Parses IFF (Interchange File Format) container files.</summary>
public static class IffReader {

  private static readonly HashSet<string> _GroupIds = ["FORM", "LIST", "CAT "];

  public static IffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IFF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffFile FromStream(Stream stream) {
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

  public static IffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 12)
      throw new InvalidDataException("Data is too small to be a valid IFF file.");

    var span = data.AsSpan();
    var outerHeader = IffChunkHeader.ReadFrom(span);
    var outerId = outerHeader.ChunkId.ToString();
    if (!_GroupIds.Contains(outerId))
      throw new InvalidDataException($"Invalid IFF signature: expected FORM, LIST, or CAT but got '{outerId}'.");

    var formType = FourCC.ReadFrom(span[IffChunkHeader.StructSize..]);
    var chunks = _ParseChunks(span, IffChunkHeader.StructSize + 4, Math.Min(data.Length, IffChunkHeader.StructSize + outerHeader.Size));

    return new IffFile { FormType = formType, Chunks = chunks };
  }

  private static List<IffChunk> _ParseChunks(ReadOnlySpan<byte> data, int offset, int end) {
    var chunks = new List<IffChunk>();

    while (offset + IffChunkHeader.StructSize <= end) {
      var header = IffChunkHeader.ReadFrom(data[offset..]);
      var chunkId = header.ChunkId.ToString();
      var chunkSize = header.Size;
      var dataStart = offset + IffChunkHeader.StructSize;

      if (_GroupIds.Contains(chunkId)) {
        if (dataStart + 4 > end)
          break;

        var subFormType = FourCC.ReadFrom(data[dataStart..]);
        var subEnd = Math.Min(end, dataStart + chunkSize);
        var subChunks = _ParseChunks(data, dataStart + 4, subEnd);
        var groupData = dataStart + chunkSize <= data.Length
          ? data[dataStart..Math.Min(data.Length, dataStart + chunkSize)].ToArray()
          : data[dataStart..data.Length].ToArray();

        chunks.Add(new IffChunk { ChunkId = header.ChunkId, Data = groupData, SubChunks = subChunks });
      } else {
        var dataEnd = Math.Min(end, dataStart + chunkSize);
        var chunkData = data[dataStart..dataEnd].ToArray();
        chunks.Add(new IffChunk { ChunkId = header.ChunkId, Data = chunkData });
      }

      // IFF chunks are word-aligned (2-byte boundary)
      offset = dataStart + chunkSize + (chunkSize & 1);
    }

    return chunks;
  }
}
