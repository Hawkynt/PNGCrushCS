using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Riff;

/// <summary>Parses RIFF container files.</summary>
public static class RiffReader {

  public static RiffFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RIFF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RiffFile FromStream(Stream stream) {
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

  public static RiffFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 12)
      throw new InvalidDataException("Data is too small to be a valid RIFF file.");

    var span = data.AsSpan();
    var riffHeader = RiffHeader.ReadFrom(span);
    if (riffHeader.ChunkId.ToString() != "RIFF")
      throw new InvalidDataException("Invalid RIFF signature.");

    var fileSize = riffHeader.Size;
    var formType = riffHeader.FormType;

    var chunks = new List<RiffChunk>();
    var lists = new List<RiffList>();
    var offset = RiffHeader.StructSize;
    var end = Math.Min(data.Length, (int)(fileSize + 8));

    _ParseElements(span, offset, end, chunks, lists);

    return new RiffFile { FormType = formType, Chunks = chunks, Lists = lists };
  }

  private static void _ParseElements(ReadOnlySpan<byte> data, int offset, int end, List<RiffChunk> chunks, List<RiffList> lists) {
    while (offset + RiffChunkHeader.StructSize <= end) {
      var chunkHdr = RiffChunkHeader.ReadFrom(data[offset..]);
      var chunkId = chunkHdr.ChunkId;
      var chunkSize = (int)chunkHdr.Size;
      var dataStart = offset + RiffChunkHeader.StructSize;

      if (chunkId.ToString() == "LIST") {
        if (dataStart + 4 > end)
          break;

        var listType = FourCC.ReadFrom(data[dataStart..]);
        var subChunks = new List<RiffChunk>();
        var subLists = new List<RiffList>();
        _ParseElements(data, dataStart + 4, Math.Min(end, dataStart + chunkSize), subChunks, subLists);
        lists.Add(new RiffList { ListType = listType, Chunks = subChunks, SubLists = subLists });
      } else {
        var dataEnd = Math.Min(end, dataStart + chunkSize);
        var chunkData = data[dataStart..dataEnd].ToArray();
        chunks.Add(new RiffChunk { Id = chunkId, Data = chunkData });
      }

      // RIFF chunks are word-aligned (2-byte boundary)
      offset = dataStart + chunkSize + (chunkSize & 1);
    }
  }
}
