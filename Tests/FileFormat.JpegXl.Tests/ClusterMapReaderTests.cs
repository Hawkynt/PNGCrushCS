using System;
using System.Collections.Generic;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Spec-conformance tests for <see cref="JxlEntropyDecoder"/>'s
/// <c>_ReadClusterMap</c> (ISO/IEC 18181-1 §C.4; libjxl
/// <c>DecodeContextMap</c> in <c>lib/jxl/dec_context_map.cc</c>).
///
/// <para>The bit reader is LSB-first: the first bit on the wire is bit 0 of
/// the first byte. Tests construct synthetic bitstreams via
/// <see cref="BitsBuilder"/> that pack named bit sequences into bytes
/// preserving that order.</para>
/// </summary>
[TestFixture]
public sealed class ClusterMapReaderTests {

  /// <summary>
  /// Helper that packs bits LSB-first into bytes, matching the
  /// <see cref="JxlBitReader"/> wire ordering. (Duplicated from
  /// PrefixCodeReaderTests to keep these test files independent.)
  /// </summary>
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
      // Pad to a comfortable size so the reader can refill without overrunning.
      while (copy.Count < 16)
        copy.Add(0);
      return copy.ToArray();
    }
  }

  // ============================================================
  // SHORT-CIRCUIT: numClusters == 1
  // ============================================================

  /// <summary>
  /// When <c>numClusters == 1</c>, all entries are 0 and no bits are consumed
  /// (libjxl: caller never calls DecodeContextMap when num_contexts == 1, but
  /// our internal helper short-circuits identically).
  /// </summary>
  [Test]
  public void ReadClusterMap_SingleCluster_AllZerosAndNoBitsConsumed() {
    var bits = new BitsBuilder().Add(0xFF, 8); // arbitrary payload — must NOT be touched
    var reader = new JxlBitReader(bits.ToBytes(), 0);
    var clusterMap = new int[5];

    JxlEntropyDecoder._ReadClusterMap(reader, clusterMap, numContexts: 5, numClusters: 1);

    Assert.Multiple(() => {
      Assert.That(clusterMap, Is.EqualTo(new[] { 0, 0, 0, 0, 0 }));
      Assert.That(reader.BitsRead, Is.EqualTo(0L), "Single-cluster short-circuit must not consume bits.");
    });
  }

  // ============================================================
  // SIMPLE MODE
  // ============================================================

  /// <summary>
  /// Simple mode, <c>bits_per_entry == 0</c>: every entry is 0. Total bits
  /// consumed = 1 (is_simple) + 2 (bits_per_entry) = 3.
  /// </summary>
  [Test]
  public void ReadClusterMap_SimpleBitsPerEntryZero_AllEntriesZero() {
    var bits = new BitsBuilder()
      .Add(1, 1)  // is_simple = 1
      .Add(0, 2); // bits_per_entry = 0 -> all zeros, no per-entry bits
    var reader = new JxlBitReader(bits.ToBytes(), 0);
    var clusterMap = new int[6];

    JxlEntropyDecoder._ReadClusterMap(reader, clusterMap, numContexts: 6, numClusters: 4);

    Assert.Multiple(() => {
      Assert.That(clusterMap, Is.EqualTo(new[] { 0, 0, 0, 0, 0, 0 }));
      Assert.That(reader.BitsRead, Is.EqualTo(3L));
    });
  }

  /// <summary>
  /// Simple mode, <c>bits_per_entry == 2</c>: each entry is a 2-bit value.
  /// Build a stream that yields the sequence {0, 3, 1, 2}; verify decoded
  /// cluster map matches and the exact bit count was consumed.
  /// </summary>
  [Test]
  public void ReadClusterMap_SimpleBitsPerEntryTwo_DecodesEachEntryAsTwoBits() {
    const int numContexts = 4;
    const int numClusters = 4; // ensures 2-bit values are always in-range
    var bits = new BitsBuilder()
      .Add(1, 1)  // is_simple = 1
      .Add(2, 2)  // bits_per_entry = 2
      .Add(0, 2)  // entry[0] = 0
      .Add(3, 2)  // entry[1] = 3
      .Add(1, 2)  // entry[2] = 1
      .Add(2, 2); // entry[3] = 2
    var reader = new JxlBitReader(bits.ToBytes(), 0);
    var clusterMap = new int[numContexts];

    JxlEntropyDecoder._ReadClusterMap(reader, clusterMap, numContexts, numClusters);

    Assert.Multiple(() => {
      Assert.That(clusterMap, Is.EqualTo(new[] { 0, 3, 1, 2 }));
      Assert.That(reader.BitsRead, Is.EqualTo(1L + 2L + 4L * 2L),
        "Bit budget = 1 (is_simple) + 2 (bits_per_entry) + numContexts * bits_per_entry.");
    });
  }

  /// <summary>
  /// Simple mode with a per-entry value that exceeds <c>numClusters</c>: the
  /// decoder must reject the bitstream rather than silently clamp. Values are
  /// in <c>[0, 1 &lt;&lt; bits_per_entry)</c>, but the spec requires every entry
  /// to be a valid cluster index <c>&lt; numClusters</c>.
  /// </summary>
  [Test]
  public void ReadClusterMap_SimpleEntryExceedsNumClusters_Throws() {
    // bits_per_entry = 2 -> 4 possible values, but numClusters = 3 -> value 3 is invalid.
    var bits = new BitsBuilder()
      .Add(1, 1)  // is_simple = 1
      .Add(2, 2)  // bits_per_entry = 2
      .Add(3, 2); // entry[0] = 3 — out of range for numClusters = 3
    var reader = new JxlBitReader(bits.ToBytes(), 0);
    var clusterMap = new int[1];

    Assert.Throws<System.IO.InvalidDataException>(
      () => JxlEntropyDecoder._ReadClusterMap(reader, clusterMap, numContexts: 1, numClusters: 3),
      "Simple-mode entry >= numClusters must be rejected.");
  }

  // ============================================================
  // INVERSE MOVE-TO-FRONT
  // ============================================================

  /// <summary>
  /// The inverse-MTF transform per ISO/IEC 18181-1 §C.4 (libjxl
  /// <c>InverseMoveToFrontTransform</c>) is exercised here directly via
  /// reflection on the private helper, because we want fixture-style
  /// validation of the algorithm independent of the surrounding bitstream
  /// parsing. We hand-craft an input/output pair:
  ///
  /// <para>Initial mtf list is identity <c>[0,1,2,3,...,255]</c>. For each
  /// input value <c>v</c>, output is <c>mtf[v]</c>, then <c>mtf[v]</c> moves
  /// to position 0. Tracing input <c>[3, 0, 0, 1, 2]</c> against the identity
  /// table:</para>
  /// <code>
  /// step | input v | mtf[v] -> out | mtf after move-to-front
  ///  0   |    3    |       3       | [3, 0, 1, 2, 4, 5, ...]
  ///  1   |    0    |       3       | [3, 0, 1, 2, 4, 5, ...]   (v=0: no move)
  ///  2   |    0    |       3       | [3, 0, 1, 2, 4, 5, ...]   (v=0: no move)
  ///  3   |    1    |       0       | [0, 3, 1, 2, 4, 5, ...]
  ///  4   |    2    |       1       | [1, 0, 3, 2, 4, 5, ...]
  /// </code>
  /// Output: <c>[3, 3, 3, 0, 1]</c>.
  /// </summary>
  [Test]
  public void InverseMoveToFront_HandTracedSequence_MatchesExpected() {
    var input = new[] { 3, 0, 0, 1, 2 };
    var expected = new[] { 3, 3, 3, 0, 1 };

    InvokeInverseMoveToFront(input, input.Length);

    Assert.That(input, Is.EqualTo(expected));
  }

  /// <summary>
  /// Identity check: an all-zeros input must remain all-zeros (every step
  /// looks up <c>mtf[0] = 0</c> and triggers no list mutation since
  /// <c>index == 0</c> is the no-op case).
  /// </summary>
  [Test]
  public void InverseMoveToFront_AllZeros_StaysAllZeros() {
    var input = new[] { 0, 0, 0, 0, 0, 0 };
    InvokeInverseMoveToFront(input, input.Length);
    Assert.That(input, Is.EqualTo(new[] { 0, 0, 0, 0, 0, 0 }));
  }

  /// <summary>
  /// MTF on the sequence <c>[1, 1, 1]</c>: each step looks up <c>mtf[1]</c>
  /// and moves it to position 0. After step 0 the list becomes
  /// <c>[1, 0, 2, 3, ...]</c>; mtf[1] is now 0. After step 1 the list becomes
  /// <c>[0, 1, 2, 3, ...]</c>; mtf[1] is 1. After step 2: list back to
  /// <c>[1, 0, 2, 3, ...]</c>; mtf[1] is 0. Output: <c>[1, 0, 1]</c>.
  /// </summary>
  [Test]
  public void InverseMoveToFront_RepeatedOnes_AlternatesOutput() {
    var input = new[] { 1, 1, 1 };
    InvokeInverseMoveToFront(input, input.Length);
    Assert.That(input, Is.EqualTo(new[] { 1, 0, 1 }));
  }

  /// <summary>
  /// Helper: invoke the private static <c>_InverseMoveToFront</c> via
  /// reflection. Keeps the helper itself private (callers need not see it).
  /// </summary>
  private static void InvokeInverseMoveToFront(int[] data, int length) {
    var method = typeof(JxlEntropyDecoder).GetMethod(
      "_InverseMoveToFront",
      System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    Assert.That(method, Is.Not.Null, "Private helper _InverseMoveToFront must exist on JxlEntropyDecoder.");
    method!.Invoke(null, new object[] { data, length });
  }
}
