using System.Collections.Generic;
using FileFormat.Riff;

namespace FileFormat.Iff;

/// <summary>A single IFF chunk with a four-character ID, raw data, and optional sub-chunks for group types.</summary>
public sealed class IffChunk {
  public required FourCC ChunkId { get; init; }
  public required byte[] Data { get; init; }
  public IReadOnlyList<IffChunk>? SubChunks { get; init; }
}
