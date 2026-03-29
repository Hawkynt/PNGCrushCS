namespace FileFormat.Riff;

/// <summary>A single RIFF chunk with a four-character ID and raw data.</summary>
public sealed class RiffChunk {
  public required FourCC Id { get; init; }
  public required byte[] Data { get; init; }
}
