namespace FileFormat.Rpl;

/// <summary>One line of the chunk catalogue: a chunk's own absolute file offset and the byte length of
/// its video and sound payloads, sitting contiguously at that offset — video first, then sound.</summary>
public readonly record struct RplChunkEntry(long FileOffset, long VideoByteSize, long AudioByteSize);
