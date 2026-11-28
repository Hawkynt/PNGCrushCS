using System.IO;

namespace FileFormat.Eri;

/// <summary>The adaptive Huffman tree of the ERINA codes (ERI's second level).</summary>
/// <remarks>
/// A tree starts with no leaves at all: the first symbol is sent as an escape, implicitly, and
/// arrives as plain bits. From then on the tree holds the symbols seen and one escape leaf; a
/// symbol not in the tree is sent as the escape's code followed by the plain bits again.
/// <para/>
/// The nodes live in one array ordered by weight, root last, children always below their parent,
/// and each pair of siblings side by side — the arrangement that lets a weight be incremented by
/// walking up the tree, swapping a node with the highest-placed node of its old weight on the way.
/// New leaves are taken from below the lowest node, which is split into the new leaf and itself.
/// When the root's weight reaches 4000H every leaf's weight is halved, rounding up, and the tree is
/// rebuilt from the leaves. A decoder has to make exactly the same swaps and splits as the encoder,
/// because the position of a node is its code.
/// </remarks>
internal sealed class EriHuffmanTree {

  private const int _ROOT = 0x200;
  private const int _NONE = 0x8000;
  private const int _LEAF = unchecked((int)0x80000000);
  private const int _ESCAPE_CODE = 0x7FFFFFFF;
  private const int _WEIGHT_LIMIT = 0x4000;

  private readonly int[] _weight = new int[_ROOT + 1];
  private readonly int[] _parent = new int[_ROOT + 1];
  private readonly int[] _child = new int[_ROOT + 1];
  private int _escape;
  private int _free;

  public EriHuffmanTree() => this.Reset();

  /// <summary>Back to the empty tree.</summary>
  public void Reset() {
    this._escape = _NONE;
    this._free = _ROOT;
    this._weight[_ROOT] = 0;
    this._parent[_ROOT] = _NONE;
    this._child[_ROOT] = _NONE;
  }

  /// <summary>The root's weight, which the order-0 coder stops counting at.</summary>
  public int TotalWeight => this._weight[_ROOT];

  private static bool _IsLeaf(int child) => (child & _LEAF) != 0;

  /// <summary>Decodes one symbol; <c>null</c> stands for the escape.</summary>
  /// <param name="bits">The stream.</param>
  /// <param name="count">Whether the decoded leaf is to be counted.</param>
  public int? Decode(EriBitReader bits, bool count) {
    if (this._escape == _NONE)
      return null;

    var child = this._child[_ROOT];
    int entry;
    do {
      entry = child + bits.ReadBit();
      if (entry < 0 || entry > _ROOT)
        throw new InvalidDataException("ERINA code walked off its Huffman tree.");
      child = this._child[entry];
    } while (!_IsLeaf(child));

    if (count)
      this._Increment(entry);

    var code = child & ~_LEAF;
    return code == _ESCAPE_CODE ? null : code;
  }

  private void _Increment(int entry) {
    ++this._weight[entry];
    this._Normalize(entry);
    if (this._weight[_ROOT] >= _WEIGHT_LIMIT)
      this._HalveAndRebuild();
  }

  private void _Recount(int parent) {
    var child = this._child[parent];
    this._weight[parent] = this._weight[child] + this._weight[child + 1];
  }

  /// <summary>Points whatever <paramref name="node"/>'s content refers to back at <paramref name="position"/>.</summary>
  private void _Relink(int node, int position) {
    var child = this._child[node];
    if (!_IsLeaf(child)) {
      this._parent[child] = position;
      this._parent[child + 1] = position;
    } else if ((child & ~_LEAF) == _ESCAPE_CODE)
      this._escape = position;
  }

  private void _Normalize(int entry) {
    while (entry < _ROOT) {
      var weight = this._weight[entry];
      var swap = entry + 1;
      while (swap < _ROOT && this._weight[swap] < weight)
        ++swap;
      --swap;

      if (swap == entry) {
        entry = this._parent[entry];
        this._Recount(entry);
        continue;
      }

      this._Relink(entry, swap);
      this._Relink(swap, entry);

      // The contents change places; each position keeps its own parent.
      (this._weight[entry], this._weight[swap]) = (this._weight[swap], this._weight[entry]);
      (this._child[entry], this._child[swap]) = (this._child[swap], this._child[entry]);

      var swapParent = this._parent[swap];
      this._Recount(swapParent);
      entry = swapParent;
    }
  }

  /// <summary>Adds a symbol the tree has not seen, after its escape.</summary>
  public void Add(int code) {
    if (this._free > 0) {
      var i = this._free -= 2;
      this._weight[i] = 1;
      this._child[i] = _LEAF | code;

      if (this._child[_ROOT] != _NONE) {
        // The lowest node is split: it moves down one place and shares a new parent with the leaf.
        this._weight[i + 1] = this._weight[i + 2];
        this._parent[i + 1] = this._parent[i + 2];
        this._child[i + 1] = this._child[i + 2];
        if (_IsLeaf(this._child[i + 1]) && (this._child[i + 1] & ~_LEAF) == _ESCAPE_CODE)
          this._escape = i + 1;

        this._weight[i + 2] = this._weight[i] + this._weight[i + 1];
        this._parent[i + 2] = this._parent[i + 1];
        this._child[i + 2] = i;
        this._parent[i] = i + 2;
        this._parent[i + 1] = i + 2;
        this._Normalize(i + 2);
      } else {
        this._parent[i] = _ROOT;
        this._escape = i + 1;
        this._weight[i + 1] = 1;
        this._parent[i + 1] = _ROOT;
        this._child[i + 1] = _LEAF | _ESCAPE_CODE;
        this._weight[_ROOT] = 2;
        this._child[_ROOT] = i;
      }

      return;
    }

    // The tree is full: the least frequent symbol gives its leaf up to the new one.
    var lowest = this._free;
    if (this._child[lowest] == (_LEAF | _ESCAPE_CODE))
      ++lowest;
    this._child[lowest] = _LEAF | code;
  }

  private void _HalveAndRebuild() {
    // Every leaf, its weight halved, gathered at the top of the array in the order they stood.
    var next = _ROOT;
    for (var i = _ROOT - 1; i >= this._free; --i) {
      if (!_IsLeaf(this._child[i]))
        continue;
      this._weight[next] = (this._weight[i] + 1) >> 1;
      this._parent[next] = this._parent[i];
      this._child[next] = this._child[i];
      --next;
    }
    ++next;

    // Then the two lightest entries at a time are joined, their parent slotted back into the
    // weight order ahead of any entry of equal weight.
    var at = this._free;
    while (true) {
      this._Copy(next, at);
      this._Copy(next + 1, at + 1);
      next += 2;

      this._Adopt(at);
      this._Adopt(at + 1);
      var weight = this._weight[at] + this._weight[at + 1];

      if (next <= _ROOT) {
        var j = next;
        while (true) {
          if (weight <= this._weight[j]) {
            this._weight[j - 1] = weight;
            this._child[j - 1] = at;
            break;
          }

          this._Copy(j, j - 1);
          if (++j > _ROOT) {
            this._weight[_ROOT] = weight;
            this._child[_ROOT] = at;
            break;
          }
        }

        --next;
      } else {
        this._weight[_ROOT] = weight;
        this._parent[_ROOT] = _NONE;
        this._child[_ROOT] = at;
        this._parent[at] = _ROOT;
        this._parent[at + 1] = _ROOT;
        break;
      }

      at += 2;
    }
  }

  private void _Copy(int from, int to) {
    this._weight[to] = this._weight[from];
    this._parent[to] = this._parent[from];
    this._child[to] = this._child[from];
  }

  private void _Adopt(int position) {
    var child = this._child[position];
    if (!_IsLeaf(child)) {
      this._parent[child] = position;
      this._parent[child + 1] = position;
    } else if ((child & ~_LEAF) == _ESCAPE_CODE)
      this._escape = position;
  }
}
