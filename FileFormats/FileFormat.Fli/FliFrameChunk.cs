namespace FileFormat.Fli;

/// <summary>A single sub-chunk within a FLI/FLC frame.</summary>
public sealed class FliFrameChunk {
  public FliChunkType ChunkType { get; init; }
  public byte[] Data { get; init; } = [];
}
