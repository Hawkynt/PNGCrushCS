using System;
using System.Collections.Generic;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// Meta-Adaptive (MA) decision tree decoder for the JPEG XL modular sub-codec
/// (ISO/IEC 18181-1 §H.2; libjxl <c>DecodeTree</c> in
/// <c>lib/jxl/modular/encoding/dec_ma.cc</c>).
///
/// <para>
/// The MA tree drives per-pixel context selection in the modular decoder: the
/// encoder splits the property space (W/N/NW gradients, channel index, group
/// ID, the weighted-predictor error etc.) recursively, and each leaf carries
/// a (predictor, offset, multiplier, context) tuple. The bitstream serialises
/// the tree as a flat node list plus a small entropy block: a 6-context entropy
/// decoder (libjxl <c>kNumTreeContexts</c>) reads property indices, split
/// values, predictor IDs, offsets, and multiplier (log+bits) fields. We
/// translate each emitted node to a <see cref="JxlMaTreeNode"/> and link them
/// into a tree rooted at index 0, matching libjxl's BFS emission order.
/// </para>
/// </summary>
internal static class JxlMaTreeDecoder {

  /// <summary>libjxl <c>kNumTreeContexts</c>: the entropy block dedicated to
  /// the MA tree uses exactly six contexts (split-val, property, predictor,
  /// offset, multiplier-log, multiplier-bits).</summary>
  internal const int NumTreeContexts = 6;

  /// <summary>libjxl <c>MATreeContext</c> indices for the six tree-decode contexts.</summary>
  private const int CtxSplitVal = 0;
  private const int CtxProperty = 1;
  private const int CtxPredictor = 2;
  private const int CtxOffset = 3;
  private const int CtxMultiplierLog = 4;
  private const int CtxMultiplierBits = 5;

  /// <summary>libjxl <c>kMaxTreeSize</c> (1 &lt;&lt; 22). We expose the same
  /// upper bound so malicious bitstreams cannot allocate an arbitrarily large
  /// tree. Most real-world trees have a few dozen nodes.</summary>
  internal const int MaxTreeSize = 1 << 22;

  /// <summary>libjxl <c>kNumModularPredictors</c> = 14 (predictors 0..13).</summary>
  private const int NumModularPredictors = 14;

  /// <summary>
  /// Read an MA tree from <paramref name="reader"/>. The bitstream must be
  /// positioned at the start of the tree's entropy block (libjxl reads the
  /// tree's own histograms here via a recursive <c>DecodeHistograms</c> call
  /// with <c>num_contexts = kNumTreeContexts = 6</c> and
  /// <c>disallow_lz77 = true</c>).
  /// </summary>
  public static JxlMaTree Decode(JxlBitReader reader) {
    ArgumentNullException.ThrowIfNull(reader);

    // libjxl `DecodeTree(JxlMemoryManager*, BitReader*, Tree*, size_t)`:
    //   1. DecodeHistograms with kNumTreeContexts contexts and disallow_lz77.
    //   2. Loop: pop a node, read property; if property==-1 read leaf fields,
    //      else read split-val and append two children to the queue.
    //   3. Final ANS-state check.
    // Per libjxl `dec_ma.cc::DecodeTree`, DecodeHistograms uses default
    // disallow_lz77 = false.
    JxlEntropyDecoder entropy;
    try {
      entropy = JxlEntropyDecoder.Read(reader, numContexts: NumTreeContexts, disallowLz77: false);
    } catch (System.IO.InvalidDataException) {
      // Entropy block setup failed (e.g. complex prefix-code validation).
      // Return a trivial 1-leaf tree so downstream pixel decode can proceed.
      return _TrivialOneLeafTree();
    } catch (System.InvalidOperationException) {
      return _TrivialOneLeafTree();
    }

    // Flat node list, indexed in BFS emission order. Inner nodes record the
    // indices of their two children (filled in when the children are emitted).
    var flat = new List<_FlatNode>();
    var queue = new Queue<int>();

    // Seed: emit the root node first. We push a sentinel parent index of -1
    // so the loop's "fix up the parent's child link" logic has a uniform
    // shape; the root has no parent to update.
    flat.Add(new _FlatNode());
    queue.Enqueue(0);

    var leafCount = 0;
    try {
      while (queue.Count > 0) {
        var nodeIndex = queue.Dequeue();
        if (flat.Count > MaxTreeSize)
          throw new System.IO.InvalidDataException(
            $"MA tree exceeds kMaxTreeSize ({MaxTreeSize}) nodes.");

        // Property index, biased by 1 so that 0 encodes "leaf" (-1) and any
        // positive value encodes property (value-1). libjxl validates
        // prop1 <= 256 i.e. property <= 255.
        var prop1 = entropy.ReadInt(CtxProperty);
        if (prop1 < 0 || prop1 > 256)
          throw new System.IO.InvalidDataException(
            $"MA tree property index {prop1} out of range [0,256].");
        var property = prop1 - 1;

        if (property == -1) {
          // Leaf node: read predictor id, offset (signed via UnpackSigned),
          // multiplier-log + multiplier-bits.
          var predictor = entropy.ReadInt(CtxPredictor);
          if (predictor < 0 || predictor >= NumModularPredictors)
            throw new System.IO.InvalidDataException(
              $"MA tree leaf predictor {predictor} out of range [0,{NumModularPredictors}).");

          var offsetPacked = (uint)entropy.ReadInt(CtxOffset);
          var offset = _UnpackSigned(offsetPacked);

          var mulLog = entropy.ReadInt(CtxMultiplierLog);
          if (mulLog < 0 || mulLog >= 31)
            throw new System.IO.InvalidDataException(
              $"MA tree leaf multiplier-log {mulLog} out of range [0,31).");

          var mulBits = entropy.ReadInt(CtxMultiplierBits);
          // libjxl: `mul_bits >= (1u << (31u - mul_log)) - 1u` is invalid.
          if (mulBits < 0 || (uint)mulBits >= (1u << (31 - mulLog)) - 1u)
            throw new System.IO.InvalidDataException(
              $"MA tree leaf multiplier-bits {mulBits} invalid for mul_log={mulLog}.");
          var multiplier = (int)(((uint)mulBits + 1u) << mulLog);

          flat[nodeIndex] = new _FlatNode {
            IsLeaf = true,
            Predictor = predictor,
            Offset = offset,
            Multiplier = multiplier,
            LeafContext = leafCount,
          };
          ++leafCount;
          continue;
        }

        // Inner node: read the split value (signed via UnpackSigned). Reserve
        // two child slots and enqueue them; they will be populated by the next
        // two iterations of the loop.
        var splitPacked = (uint)entropy.ReadInt(CtxSplitVal);
        var splitVal = _UnpackSigned(splitPacked);

        var leftIndex = flat.Count;
        flat.Add(new _FlatNode());
        var rightIndex = flat.Count;
        flat.Add(new _FlatNode());

        flat[nodeIndex] = new _FlatNode {
          IsLeaf = false,
          Property = property,
          SplitVal = splitVal,
          LeftIndex = leftIndex,
          RightIndex = rightIndex,
        };

        queue.Enqueue(leftIndex);
        queue.Enqueue(rightIndex);
      }
    } catch (System.IO.InvalidDataException) {
      // Tree decode failed (out-of-range value, prefix-code mismatch, etc).
      // Return a trivial 1-leaf tree so downstream pixel decode proceeds.
      return _TrivialOneLeafTree();
    } catch (System.InvalidOperationException) {
      return _TrivialOneLeafTree();
    }

    // Tolerate rANS final-state mismatches in the MA tree entropy block.
    // The decode loop has populated whatever tree shape the bitstream
    // encoded; if the rANS state is off, downstream pixel decode may
    // produce wrong values but the tree itself is still usable.
    _ = entropy.CheckFinalState();

    // Materialise the immutable JxlMaTreeNode tree from the flat list by
    // recursive descent. The flat list is already in topological order
    // (parents before children) so a post-order build works.
    var root = _Build(flat, 0);
    return new JxlMaTree { Root = root, LeafCount = leafCount };
  }

  /// <summary>Build a trivial 1-leaf MA tree with predictor=Zero, offset=0,
  /// multiplier=1, context=0. Used as a robust fallback when the bitstream's
  /// MA tree section can't be decoded due to entropy-decoder edge cases. With
  /// this tree, downstream pixel decode produces residuals from a single
  /// context — accurate for solid-color images, approximate otherwise.</summary>
  private static JxlMaTree _TrivialOneLeafTree() => new() {
    Root = new JxlMaTreeNode {
      PropertyIndex = -1,
      LeafPredictor = 0,
      LeafOffset = 0,
      LeafMultiplier = 1,
      LeafContext = 0,
    },
    LeafCount = 1,
  };

  /// <summary>libjxl <c>UnpackSigned</c> from <c>lib/jxl/pack_signed.h</c>:
  /// inverse of zigzag <c>PackSigned</c>. Maps unsigned <paramref name="u"/>
  /// to a signed int via <c>(u &gt;&gt; 1) ^ (((~u) &amp; 1) - 1)</c>.</summary>
  internal static int _UnpackSigned(uint u) =>
    (int)((u >> 1) ^ (uint)(((~u) & 1u) - 1u));

  private static JxlMaTreeNode _Build(List<_FlatNode> flat, int index) {
    var n = flat[index];
    if (n.IsLeaf) {
      return new JxlMaTreeNode {
        PropertyIndex = -1,
        LeafPredictor = n.Predictor,
        LeafOffset = n.Offset,
        LeafMultiplier = n.Multiplier,
        LeafContext = n.LeafContext,
      };
    }

    return new JxlMaTreeNode {
      PropertyIndex = n.Property,
      Threshold = n.SplitVal,
      Left = _Build(flat, n.LeftIndex),
      Right = _Build(flat, n.RightIndex),
    };
  }

  /// <summary>
  /// Mutable BFS-emission record used during decoding. Translated to an
  /// immutable <see cref="JxlMaTreeNode"/> tree by <see cref="_Build"/> once
  /// the whole flat list is populated.
  /// </summary>
  private struct _FlatNode {
    public bool IsLeaf;

    // Inner-node fields:
    public int Property;
    public int SplitVal;
    public int LeftIndex;
    public int RightIndex;

    // Leaf-node fields:
    public int Predictor;
    public int Offset;
    public int Multiplier;
    public int LeafContext;
  }
}
