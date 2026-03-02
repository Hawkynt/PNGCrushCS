namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  internal sealed partial class HuffmanTree {
    /// <summary>Package-Merge item: leaf (Left == -1, Right = active index) or package (Left/Right = item indices)</summary>
    private readonly record struct PMItem(long Weight, int Left, int Right) {

      public bool IsLeaf => this.Left == -1;

    }
  }
}
