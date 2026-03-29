namespace FileFormat.Ktx;

/// <summary>A single mip level within a KTX file.</summary>
public sealed class KtxMipLevel {
  public int Width { get; init; }
  public int Height { get; init; }
  public byte[] Data { get; init; } = [];
}
