using System;

namespace FileFormat.Codecs.Idcin;

/// <summary>
/// One of the 256 order-1 Huffman trees an id Cinematic file's 64KiB table builds — one tree per
/// possible previous-pixel value, built here from that value's 256-entry symbol-count histogram.
/// </summary>
/// <remarks>
/// The table states counts, not codes: building the actual tree from them is the decoder's job, and the
/// format's own documentation says only that a dictionary is built from the histogram, explicitly
/// leaving the construction itself to "look elsewhere for a more in depth discussion on Huffman coding"
/// — general knowledge of the algorithm, not a fact this format states. The construction here is the
/// textbook one: repeatedly pair the two lowest-count nodes not yet paired, until one node is left
/// unpaired. That last node is the root; leaves are the 256 symbol values themselves, so a node index
/// below 256 is a symbol and one at or above it is an internal node this tree still has to descend
/// through.
/// <para/>
/// <b>Ties are broken toward the lowest index — settled by measurement, since "the textbook
/// construction" alone does not say which of several equal-count nodes is paired first.</b> Breaking a
/// tie toward the highest index instead was tried against both real files and fails: one does not
/// finish its first picture at all, and the other manages two pictures before running out of coded
/// bits it should not have run out of. Breaking ties toward the lowest index is the one rule, among
/// every combination of tie-breaking and bit order tried, that reaches every picture of both real files
/// — forty-eight and eighty-two — with nothing left over.
/// <para/>
/// <b>A histogram with at most one nonzero count builds no internal node at all</b> — nothing pairs with
/// nothing under the construction above — <b>and the root stays at the sentinel value <c>255</c></b>,
/// the top of the leaf range the construction never wrote past, <b>regardless of which symbol (if any)
/// actually holds the nonzero count.</b> This is not a separate fact to confirm; it falls straight out
/// of the construction once that construction is fixed by the measurement above, and a context this
/// starved of data cannot arise from a real picture without every other byte in it being outside this
/// tree's alphabet too.
/// </remarks>
internal sealed class IdcinHuffmanTree {

  private const int _TOKENS = 256;

  /// <summary>The node a decode starts from. Below <see cref="_TOKENS"/>, this is itself the only
  /// symbol the tree can ever produce and no bit is read to reach it.</summary>
  public int Root { get; }

  private readonly int[] _left;
  private readonly int[] _right;

  private IdcinHuffmanTree(int root, int[] left, int[] right) {
    this.Root = root;
    this._left = left;
    this._right = right;
  }

  /// <summary>The child reached by a Huffman digit of nought, from an internal node at or above <see cref="_TOKENS"/>.</summary>
  public int Left(int internalNode) => this._left[internalNode - _TOKENS];

  /// <summary>The child reached by a Huffman digit of one, from an internal node at or above <see cref="_TOKENS"/>.</summary>
  public int Right(int internalNode) => this._right[internalNode - _TOKENS];

  public static IdcinHuffmanTree Build(ReadOnlySpan<byte> histogram) {
    if (histogram.Length < _TOKENS)
      throw new ArgumentException($"A Huffman histogram is {histogram.Length} bytes, short of the 256 counts it needs.", nameof(histogram));

    // Up to 256 leaves and, once every leaf has been paired away, up to 255 internal nodes above them.
    var count = new int[_TOKENS * 2 - 1];
    var used = new bool[_TOKENS * 2 - 1];
    for (var i = 0; i < _TOKENS; ++i)
      count[i] = histogram[i];

    var left = new int[_TOKENS - 1];
    var right = new int[_TOKENS - 1];
    var nodeCount = _TOKENS;

    while (true) {
      var first = _Smallest(count, used, nodeCount);
      if (first < 0)
        break;

      var second = _Smallest(count, used, nodeCount);
      if (second < 0)
        break;

      left[nodeCount - _TOKENS] = first;
      right[nodeCount - _TOKENS] = second;
      count[nodeCount] = count[first] + count[second];
      ++nodeCount;
    }

    return new(nodeCount - 1, left, right);
  }

  /// <summary>The lowest-count node not yet used and not already empty, or <c>-1</c> once fewer than
  /// two such nodes remain.</summary>
  private static int _Smallest(int[] count, bool[] used, int nodeCount) {
    var bestCount = int.MaxValue;
    var bestNode = -1;

    for (var i = 0; i < nodeCount; ++i) {
      if (used[i] || count[i] == 0)
        continue;
      if (count[i] >= bestCount)
        continue;

      bestCount = count[i];
      bestNode = i;
    }

    if (bestNode < 0)
      return -1;

    used[bestNode] = true;
    return bestNode;
  }
}
