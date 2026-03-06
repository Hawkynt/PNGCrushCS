namespace FileFormat.Png;

/// <summary>A parsed PNG chunk with its type and data</summary>
public readonly record struct PngChunk(string Type, byte[] Data);
