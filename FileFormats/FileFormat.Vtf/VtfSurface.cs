namespace FileFormat.Vtf;

/// <summary>A single mipmap surface within a VTF file.</summary>
public sealed class VtfSurface {
  public int Width { get; init; }
  public int Height { get; init; }
  public int MipLevel { get; init; }
  public int Frame { get; init; }
  public byte[] Data { get; init; } = [];
}
