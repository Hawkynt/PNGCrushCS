using System.Collections.Generic;

namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  /// <summary>Cached dynamic Huffman header data (RLE encoding, code-length tree, HLIT/HDIST/HCLEN)</summary>
  private readonly record struct DynamicHeader(
    List<(int code, int extraBits, int extraValue)> RleCodes,
    HuffmanTree ClTree,
    int Hlit,
    int Hdist,
    int Hclen);
}
