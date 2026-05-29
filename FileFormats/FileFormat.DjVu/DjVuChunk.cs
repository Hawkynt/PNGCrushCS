namespace FileFormat.DjVu;

/// <summary>A single DjVu IFF85 chunk with a four-character ID and raw data.</summary>
public sealed class DjVuChunk {

  /// <summary>The 4-byte ASCII chunk identifier (e.g. "INFO", "BG44", "PM44").</summary>
  public required string ChunkId { get; init; }

  /// <summary>The raw chunk payload data.</summary>
  public required byte[] Data { get; init; }
}
