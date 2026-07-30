using System;
using System.Collections.Generic;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Tests for <see cref="JxlBlockContextMap"/> (ISO/IEC 18181-1 §G.8 /
/// libjxl <c>BlockCtxMap</c> in <c>lib/jxl/ac_context.h</c> +
/// <c>DecodeBlockCtxMap</c> in <c>lib/jxl/entropy_coder.cc</c>).
///
/// <para>The default context map is the most important test case: it is the
/// bitstream the encoder emits when it sets <c>is_default = 1</c>, and is what
/// every realistic JXL decoder must support.</para>
/// </summary>
[TestFixture]
public sealed class JxlBlockContextMapTests {

  /// <summary>Bit-LSB packer matching <see cref="JxlBitReader"/> wire ordering.</summary>
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

  // ============================================================
  // CreateDefault — the libjxl `BlockCtxMap()` default constructor.
  // ============================================================

  /// <summary>
  /// libjxl's <c>kDefaultCtxMap</c> contains 39 entries with a maximum value
  /// of 14, giving <c>num_ctxs = 15</c>. This is the canonical sanity check
  /// referenced by ac_context.h and is what every default-encoded VarDCT
  /// frame uses.
  /// </summary>
  [Test]
  public void CreateDefault_HasFifteenContexts() {
    var bcm = JxlBlockContextMap.CreateDefault();
    Assert.That(bcm.NumContexts, Is.EqualTo(15),
      "libjxl kDefaultCtxMap has max value 14 → 15 distinct contexts.");
  }

  /// <summary>
  /// libjxl <c>BlockCtxMap::NumACContexts() = num_ctxs * (kNonZeroBuckets +
  /// kZeroDensityContextCount) = num_ctxs * 495</c>. For the default map
  /// (num_ctxs = 15) this is 15 × 495 = 7425. This is what
  /// <c>ProcessACGlobal</c> passes to <c>DecodeHistograms</c> for the AC
  /// entropy block.
  /// </summary>
  [Test]
  public void CreateDefault_NumACContexts_Is7425() {
    var bcm = JxlBlockContextMap.CreateDefault();
    Assert.That(bcm.NumACContexts, Is.EqualTo(15 * 495));
  }

  /// <summary>
  /// Default map size: 39 entries (3 channels × 13 orders × 1 dc × 1 qf).
  /// We assert this indirectly by exercising every (channel, ord, qf=0)
  /// combination through GetContext and checking that all returns are in
  /// [0, num_ctxs).
  /// </summary>
  [Test]
  public void CreateDefault_AllContextsInRange() {
    var bcm = JxlBlockContextMap.CreateDefault();
    var seen = new HashSet<int>();

    Assert.Multiple(() => {
      for (var c = 0; c < 3; ++c) {
        for (var s = 0; s < 27; ++s) { // 27 = AcStrategyType.Dct128x256 + 1
          var ctx = bcm.GetContext(c, (JxlAcStrategyType)s, qfIndex: 0);
          Assert.That(ctx, Is.InRange(0, bcm.NumContexts - 1),
            $"Context for (c={c}, s={s}, qf=0) out of range: {ctx}");
          seen.Add(ctx);
        }
      }
      Assert.That(seen.Count, Is.EqualTo(bcm.NumContexts),
        "Every claimed context must actually be reachable through GetContext.");
    });
  }

  /// <summary>
  /// Spot-checks against libjxl's hard-coded <c>kDefaultCtxMap</c>:
  /// <list type="bullet">
  ///   <item>(channel=Y, ord=0, qf=0) → ctx_map[0] = 0</item>
  ///   <item>(channel=X, ord=0, qf=0) → ctx_map[13] = 7</item>
  ///   <item>(channel=B, ord=0, qf=0) → ctx_map[26] = 7</item>
  ///   <item>(channel=Y, ord=12, qf=0) → ctx_map[12] = 6</item>
  ///   <item>(channel=X, ord=8, qf=0) → ctx_map[13+8] = 14</item>
  /// </list>
  /// Channel mapping: Y is c=1, X is c=0, B is c=2 (libjxl swaps X/Y via
  /// <c>c &lt; 2 ? c ^ 1 : 2</c>).
  /// </summary>
  [Test]
  public void GetContext_DefaultMap_MatchesLibJxlValues() {
    var bcm = JxlBlockContextMap.CreateDefault();

    Assert.Multiple(() => {
      // (Y=1, ord=0, qf=0) — channel_bucket = 1^1 = 0, idx = 0*13 + 0 = 0 → 0
      Assert.That(bcm.GetContext(1, JxlAcStrategyType.Dct8x8, 0), Is.EqualTo(0));
      // (X=0, ord=0, qf=0) — channel_bucket = 0^1 = 1, idx = 1*13 + 0 = 13 → 7
      Assert.That(bcm.GetContext(0, JxlAcStrategyType.Dct8x8, 0), Is.EqualTo(7));
      // (B=2, ord=0, qf=0) — channel_bucket = 2, idx = 2*13 + 0 = 26 → 7
      Assert.That(bcm.GetContext(2, JxlAcStrategyType.Dct8x8, 0), Is.EqualTo(7));
      // (Y=1, ord=StrategyOrder[Dct8x4=13]=1) → idx = 0*13+1 = 1 → ctx_map[1] = 1
      Assert.That(bcm.GetContext(1, JxlAcStrategyType.Dct8x4, 0), Is.EqualTo(1));
      // (X=0, ord=StrategyOrder[Dct32x8=8]=5) → idx = 1*13+5 = 18 → ctx_map[18] = 11
      Assert.That(bcm.GetContext(0, JxlAcStrategyType.Dct32x8, 0), Is.EqualTo(11));
    });
  }

  /// <summary>
  /// Argument validation: an out-of-range channel index throws.
  /// </summary>
  [Test]
  public void GetContext_InvalidChannel_Throws() {
    var bcm = JxlBlockContextMap.CreateDefault();
    Assert.Throws<ArgumentOutOfRangeException>(
      () => bcm.GetContext(3, JxlAcStrategyType.Dct8x8, 0));
  }

  /// <summary>
  /// Argument validation: a qfIndex beyond the threshold count throws.
  /// Default map has zero qf thresholds → only qfIndex=0 is valid.
  /// </summary>
  [Test]
  public void GetContext_QfIndexOutOfRange_Throws() {
    var bcm = JxlBlockContextMap.CreateDefault();
    Assert.Throws<ArgumentOutOfRangeException>(
      () => bcm.GetContext(0, JxlAcStrategyType.Dct8x8, 1),
      "Default map has no qf thresholds → qfIndex=1 is out of range.");
  }

  // ============================================================
  // Decode — bitstream parsing
  // ============================================================

  /// <summary>
  /// Bitstream <c>is_default = 1</c>: <see cref="JxlBlockContextMap.Decode"/>
  /// must return the default map and consume exactly 1 bit (the flag itself).
  /// </summary>
  [Test]
  public void Decode_IsDefaultFlagOne_ReturnsDefaultMap() {
    var bits = new BitsBuilder().Add(1, 1).ToBytes(); // is_default = 1
    var reader = new JxlBitReader(bits, 0);
    var entropy = JxlEntropyDecoder.CreateSimple(reader, 1, 0);

    var bcm = JxlBlockContextMap.Decode(reader, entropy);

    Assert.Multiple(() => {
      Assert.That(bcm.NumContexts, Is.EqualTo(15),
        "Default map has 15 contexts.");
      Assert.That(reader.BitsRead, Is.EqualTo(1L),
        "Should consume exactly the is_default flag.");
      // Spot check against the default map values.
      Assert.That(bcm.GetContext(1, JxlAcStrategyType.Dct8x8, 0), Is.EqualTo(0));
      Assert.That(bcm.GetContext(0, JxlAcStrategyType.Dct8x8, 0), Is.EqualTo(7));
    });
  }

  /// <summary>
  /// Constants exposed for downstream code: number of size-class orders is 13
  /// (libjxl <c>kNumOrders</c>), and the strategy-order LUT has exactly 27
  /// entries (one per <c>AcStrategyType</c>).
  /// </summary>
  [Test]
  public void Constants_MatchLibJxl() {
    Assert.Multiple(() => {
      Assert.That(JxlBlockContextMap.NumOrders, Is.EqualTo(13),
        "kNumOrders from coeff_order_fwd.h.");
      Assert.That(JxlBlockContextMap.StrategyOrder.Length, Is.EqualTo(27),
        "One order entry per AcStrategyType (DCT8 .. DCT128X256).");
      Assert.That(JxlBlockContextMap.DefaultCtxMap.Length, Is.EqualTo(39),
        "3 channels × 13 orders × 1 dc × 1 qf bucket = 39.");
    });
  }
}
