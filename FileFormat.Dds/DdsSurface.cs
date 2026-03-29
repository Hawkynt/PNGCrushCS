namespace FileFormat.Dds;

/// <summary>A single mip-level surface within a DDS file.</summary>
public sealed class DdsSurface {
  public int Width { get; init; }
  public int Height { get; init; }
  public int MipLevel { get; init; }
  public byte[] Data { get; init; } = [];
}
