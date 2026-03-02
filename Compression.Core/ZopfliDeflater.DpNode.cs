namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  /// <summary>A node in the DP optimal-parse graph</summary>
  internal struct DpNode {
    public long Cost;
    public int Length;
    public int Distance;
  }
}
