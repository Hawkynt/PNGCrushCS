using System;
using System.Collections.Generic;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Spec-conformance tests for <see cref="JxlMaTreeDecoder"/> (ISO/IEC 18181-1
/// §H.2; libjxl <c>DecodeTree</c> in <c>lib/jxl/modular/encoding/dec_ma.cc</c>).
///
/// <para>
/// The MA tree's entropy block is the recursive 6-context entropy decoder
/// (<c>kNumTreeContexts</c>) read at the top of <see cref="JxlMaTreeDecoder.Decode"/>.
/// To craft a deterministic bitstream we use a degenerate single-symbol prefix
/// code: every <c>entropy.ReadInt(context)</c> returns 0, which the MA tree
/// loop interprets as <c>property = -1</c> (leaf), predictor 0, offset 0,
/// mult-log 0, mult-bits 0 (multiplier = 1).
/// </para>
/// </summary>
[TestFixture]
public sealed class JxlMaTreeTests {

  /// <summary>LSB-first bit packer matching <see cref="JxlBitReader"/>'s wire order.</summary>
  private sealed class BitsBuilder {
    private readonly List<byte> _bytes = new();
    private byte _current;
    private int _bitInByte;

    public BitsBuilder Add(int value, int nBits) {
      for (var i = 0; i < nBits; ++i) {
        var bit = (value >> i) & 1;
        _current |= (byte)(bit << _bitInByte);
        ++_bitInByte;
        if (_bitInByte == 8) {
          _bytes.Add(_current);
          _current = 0;
          _bitInByte = 0;
        }
      }
      return this;
    }

    public byte[] ToBytes() {
      var copy = new List<byte>(_bytes);
      if (_bitInByte != 0)
        copy.Add(_current);
      while (copy.Count < 32)
        copy.Add(0);
      return copy.ToArray();
    }
  }

  /// <summary>
  /// Build the prefix of bits that constructs a degenerate 6-context entropy
  /// decoder where every <c>ReadInt</c> returns 0.
  /// Layout (libjxl <c>DecodeHistograms</c> + <c>DecodeContextMap</c>):
  ///   <c>lz77_enabled = 0</c>           (1 bit)
  ///   cluster map (since num_contexts &gt; 1):
  ///     <c>is_simple = 1</c>            (1 bit)
  ///     <c>bits_per_entry = 0</c>       (2 bits) → all 6 contexts map to cluster 0
  ///   <c>use_prefix_code = 1</c>        (1 bit)
  ///   (log_alpha_size: skipped — prefix codes hardwire 15)
  ///   <c>split_exponent = 0</c>         (4 bits)
  ///   (msb / lsb fields: 0-bit reads when split_exponent==0)
  ///   <c>alphabet_size selector bit = 0</c> (1 bit; <c>DecodeVarLenUint16</c>
  ///     returns 0, +1 -&gt; alphabet_size = 1, single-symbol code, 0 bits to decode)
  /// Total: 10 bits before the MA tree decode loop starts consuming tokens.
  /// </summary>
  private static BitsBuilder _PrefixDegenerateEntropy(BitsBuilder b) =>
    b.Add(0, 1)   // lz77_enabled = false
     .Add(1, 1)   // cluster map is_simple = true
     .Add(0, 2)   // cluster map bits_per_entry = 0 (all entries → cluster 0)
     .Add(1, 1)   // use_prefix_code = true
     .Add(0, 4)   // split_exponent = 0
     .Add(0, 1);  // alphabet_size: VarLenUint16 first bit = 0, so size = 1

  [Test]
  public void Decode_SingleLeafTree_ReturnsOneLeaf() {
    var bits = _PrefixDegenerateEntropy(new BitsBuilder()).ToBytes();
    var reader = new JxlBitReader(bits, 0);

    var tree = JxlMaTreeDecoder.Decode(reader);

    Assert.Multiple(() => {
      Assert.That(tree.LeafCount, Is.EqualTo(1));
      Assert.That(tree.Root.IsLeaf, Is.True, "Root should be the lone leaf.");
      Assert.That(tree.Root.PropertyIndex, Is.EqualTo(-1));
      Assert.That(tree.Root.LeafPredictor, Is.EqualTo(0));
      Assert.That(tree.Root.LeafOffset, Is.EqualTo(0));
      // mul_log = 0, mul_bits = 0 -> multiplier = (0+1) << 0 = 1
      Assert.That(tree.Root.LeafMultiplier, Is.EqualTo(1));
      Assert.That(tree.Root.LeafContext, Is.EqualTo(0));
    });
  }

  [Test]
  public void Decode_SingleLeafTree_TraverseReturnsRoot() {
    var bits = _PrefixDegenerateEntropy(new BitsBuilder()).ToBytes();
    var reader = new JxlBitReader(bits, 0);

    var tree = JxlMaTreeDecoder.Decode(reader);

    var props = new int[16];
    var leaf = tree.Traverse(props);

    Assert.That(leaf, Is.SameAs(tree.Root),
      "On a single-leaf tree any property vector traverses to the root.");
  }

  [Test]
  public void UnpackSigned_RoundTripsAcrossPositiveAndNegative() {
    // Spec: PackSigned(0)=0, PackSigned(1)=2, PackSigned(-1)=1,
    //       PackSigned(2)=4, PackSigned(-2)=3.
    // UnpackSigned is the inverse.
    Assert.Multiple(() => {
      Assert.That(JxlMaTreeDecoder._UnpackSigned(0), Is.EqualTo(0));
      Assert.That(JxlMaTreeDecoder._UnpackSigned(1), Is.EqualTo(-1));
      Assert.That(JxlMaTreeDecoder._UnpackSigned(2), Is.EqualTo(1));
      Assert.That(JxlMaTreeDecoder._UnpackSigned(3), Is.EqualTo(-2));
      Assert.That(JxlMaTreeDecoder._UnpackSigned(4), Is.EqualTo(2));
      Assert.That(JxlMaTreeDecoder._UnpackSigned(99), Is.EqualTo(-50));
      Assert.That(JxlMaTreeDecoder._UnpackSigned(100), Is.EqualTo(50));
    });
  }

  [Test]
  public void NumTreeContexts_MatchesLibjxlConstant() {
    // libjxl `kNumTreeContexts = 6` (split-val, property, predictor, offset,
    // mult-log, mult-bits). The decoder hard-wires this and passes it to
    // JxlEntropyDecoder.Read; if it ever drifts the decoder will fail to read
    // a real libjxl tree.
    Assert.That(JxlMaTreeDecoder.NumTreeContexts, Is.EqualTo(6));
  }

  [Test]
  public void MaxTreeSize_MatchesLibjxlConstant() {
    // libjxl `kMaxTreeSize = 1 << 22` from
    // lib/jxl/modular/encoding/ma_common.h.
    Assert.That(JxlMaTreeDecoder.MaxTreeSize, Is.EqualTo(1 << 22));
  }

  [Test]
  public void Decode_NullReader_Throws() {
    Assert.Throws<ArgumentNullException>(() => JxlMaTreeDecoder.Decode(null!));
  }
}
