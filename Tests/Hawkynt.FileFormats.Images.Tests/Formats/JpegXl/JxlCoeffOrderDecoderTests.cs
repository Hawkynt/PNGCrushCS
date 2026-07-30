using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Unit tests for <see cref="JxlCoeffOrderDecoder"/> — the per-AC-strategy
/// permutation decoder for VarDCT. Mirrors libjxl
/// <c>coeff_order.cc::DecodeCoeffOrders</c>.
/// </summary>
[TestFixture]
public sealed class JxlCoeffOrderDecoderTests {

  [Test]
  public void CoeffOrderContext_Zero_ReturnsZero() {
    Assert.That(JxlCoeffOrderDecoder.CoeffOrderContext(0), Is.EqualTo(0));
  }

  [Test]
  public void CoeffOrderContext_PowerOfTwo_GivesLogPlusOne() {
    // libjxl `HybridUintConfig(0, 0, 0).Encode`: token = 1 + floor(log2(val))
    // for val >= 1, capped at kPermutationContexts - 1 = 7.
    Assert.That(JxlCoeffOrderDecoder.CoeffOrderContext(1), Is.EqualTo(1));   // 1 + 0
    Assert.That(JxlCoeffOrderDecoder.CoeffOrderContext(2), Is.EqualTo(2));   // 1 + 1
    Assert.That(JxlCoeffOrderDecoder.CoeffOrderContext(4), Is.EqualTo(3));   // 1 + 2
    Assert.That(JxlCoeffOrderDecoder.CoeffOrderContext(8), Is.EqualTo(4));
    Assert.That(JxlCoeffOrderDecoder.CoeffOrderContext(16), Is.EqualTo(5));
    Assert.That(JxlCoeffOrderDecoder.CoeffOrderContext(32), Is.EqualTo(6));
    Assert.That(JxlCoeffOrderDecoder.CoeffOrderContext(64), Is.EqualTo(7));  // 1 + 6
    // Capped at 7.
    Assert.That(JxlCoeffOrderDecoder.CoeffOrderContext(128), Is.EqualTo(7));
    Assert.That(JxlCoeffOrderDecoder.CoeffOrderContext(uint.MaxValue), Is.EqualTo(7));
  }

  [Test]
  public void DecodeCoeffOrders_UsedOrdersZero_ConsumesNoBits() {
    // libjxl: when used_orders == 0 the function returns immediately
    // without reading histograms or permutations.
    var bits = new byte[] { 0xFF }; // unused
    var reader = new JxlBitReader(bits, 0);
    JxlCoeffOrderDecoder.DecodeCoeffOrders(reader, usedOrders: 0);
    Assert.That(reader.BitsRead, Is.EqualTo(0));
  }

  [Test]
  public void StrategyOrder_HasCorrectLength() {
    Assert.That(JxlCoeffOrderDecoder.StrategyOrder.Length, Is.EqualTo(JxlCoeffOrderDecoder.NumValidStrategies));
    Assert.That(JxlCoeffOrderDecoder.CoveredBlocksX.Length, Is.EqualTo(JxlCoeffOrderDecoder.NumValidStrategies));
    Assert.That(JxlCoeffOrderDecoder.CoveredBlocksY.Length, Is.EqualTo(JxlCoeffOrderDecoder.NumValidStrategies));
  }

  [Test]
  public void StrategyOrder_DCT8_IsZero() {
    // libjxl: DCT8 (strategy 0) maps to order bucket 0.
    Assert.That(JxlCoeffOrderDecoder.StrategyOrder[0], Is.EqualTo(0));
  }
}
