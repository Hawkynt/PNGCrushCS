namespace FileFormat.Wad;

/// <summary>A single lump (named data chunk) within a WAD file.</summary>
public sealed class WadLump {
  /// <summary>Lump name (up to 8 ASCII characters).</summary>
  public string Name { get; init; } = "";

  /// <summary>Raw lump data.</summary>
  public byte[] Data { get; init; } = [];
}
