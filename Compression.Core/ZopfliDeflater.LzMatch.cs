namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  /// <summary>A match candidate from the hash chain</summary>
  internal readonly record struct LzMatch(int Length, int Distance);
}
