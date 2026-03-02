namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  /// <summary>A literal (Distance=0) or length+distance pair in the LZ77 output</summary>
  internal readonly record struct LzSymbol(ushort LitLen, ushort Distance);
}
