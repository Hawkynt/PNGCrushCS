namespace FileFormat.Jpeg;

/// <summary>64-entry quantization table stored in zigzag order.</summary>
internal sealed class JpegQuantTable {
  public int TableId { get; init; }
  public int[] Values { get; init; } = new int[64];
  public bool Is16Bit { get; init; }
}
