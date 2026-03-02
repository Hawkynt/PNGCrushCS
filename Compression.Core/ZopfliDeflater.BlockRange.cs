namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  /// <summary>Symbol index range for one DEFLATE block</summary>
  internal readonly record struct BlockRange(int Start, int End);
}
