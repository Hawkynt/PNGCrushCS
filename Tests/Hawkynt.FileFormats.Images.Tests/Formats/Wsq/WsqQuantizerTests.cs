using System;
using FileFormat.Wsq;

namespace FileFormat.Wsq.Tests;

[TestFixture]
public sealed class WsqQuantizerTests {

  [Test]
  [Category("Unit")]
  public void Quantize_Dequantize_ZeroBinHandling() {
    var param = new WsqQuantizer.QuantParams(2.0, 0.88);
    var subbandParams = new WsqQuantizer.QuantParams[WsqWavelet.NUM_SUBBANDS];
    Array.Fill(subbandParams, param);

    var coeffs = new double[16];
    coeffs[0] = 0.5; // Within zero bin (|0.5| <= 0.88)
    coeffs[1] = 3.0; // Outside zero bin

    var indices = WsqQuantizer.Quantize(coeffs, 4, 4, subbandParams);

    // 0.5 is within zero bin center (0.88), should be quantized to 0
    Assert.That(indices[0], Is.EqualTo(0));
    // 3.0 is outside zero bin, should be non-zero
    Assert.That(indices[1], Is.Not.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Quantize_Dequantize_SignPreservation() {
    var param = new WsqQuantizer.QuantParams(1.0, 0.4);
    var subbandParams = new WsqQuantizer.QuantParams[WsqWavelet.NUM_SUBBANDS];
    Array.Fill(subbandParams, param);

    var coeffs = new double[16];
    coeffs[0] = 5.0;
    coeffs[1] = -5.0;

    var indices = WsqQuantizer.Quantize(coeffs, 4, 4, subbandParams);

    Assert.That(indices[0], Is.GreaterThan(0));
    Assert.That(indices[1], Is.LessThan(0));
    Assert.That(Math.Abs(indices[0]), Is.EqualTo(Math.Abs(indices[1])));
  }

  [Test]
  [Category("Unit")]
  public void Dequantize_ZeroIndex_ReturnsZero() {
    var param = new WsqQuantizer.QuantParams(2.0, 0.88);
    var subbandParams = new WsqQuantizer.QuantParams[WsqWavelet.NUM_SUBBANDS];
    Array.Fill(subbandParams, param);

    var indices = new int[16]; // all zeros
    var coeffs = WsqQuantizer.Dequantize(indices, 4, 4, subbandParams);

    for (var i = 0; i < coeffs.Length; ++i)
      Assert.That(coeffs[i], Is.EqualTo(0.0));
  }

  [Test]
  [Category("Unit")]
  public void Dequantize_NonZeroIndex_ReturnsNonZero() {
    var param = new WsqQuantizer.QuantParams(2.0, 0.88);
    var subbandParams = new WsqQuantizer.QuantParams[WsqWavelet.NUM_SUBBANDS];
    Array.Fill(subbandParams, param);

    var indices = new int[16];
    indices[0] = 3;
    indices[1] = -2;

    var coeffs = WsqQuantizer.Dequantize(indices, 4, 4, subbandParams);

    Assert.That(coeffs[0], Is.GreaterThan(0.0));
    Assert.That(coeffs[1], Is.LessThan(0.0));
  }

  [Test]
  [Category("Unit")]
  public void ComputeParams_ReturnsCorrectCount() {
    var coeffs = new double[64 * 64];
    var result = WsqQuantizer.ComputeParams(coeffs, 64, 64, 0.75);

    Assert.That(result.Length, Is.EqualTo(WsqWavelet.NUM_SUBBANDS));
  }

  [Test]
  [Category("Unit")]
  public void ComputeParams_HighQuality_NarrowerBins() {
    var coeffs = new double[32 * 32];
    for (var i = 0; i < coeffs.Length; ++i)
      coeffs[i] = Math.Sin(i * 0.1) * 50;

    var highQ = WsqQuantizer.ComputeParams(coeffs, 32, 32, 0.95);
    var lowQ = WsqQuantizer.ComputeParams(coeffs, 32, 32, 0.3);

    // Higher quality should generally produce narrower bins (smaller bin width)
    Assert.That(highQ[0].BinWidth, Is.LessThanOrEqualTo(lowQ[0].BinWidth));
  }

  [Test]
  [Category("Unit")]
  public void Quantize_Dequantize_RoundTrip_ApproximateRecovery() {
    var param = new WsqQuantizer.QuantParams(1.0, 0.4);
    var subbandParams = new WsqQuantizer.QuantParams[WsqWavelet.NUM_SUBBANDS];
    Array.Fill(subbandParams, param);

    var coeffs = new double[16];
    coeffs[0] = 10.0;
    coeffs[1] = -7.5;
    coeffs[2] = 0.0;
    coeffs[3] = 3.2;

    var indices = WsqQuantizer.Quantize(coeffs, 4, 4, subbandParams);
    var restored = WsqQuantizer.Dequantize(indices, 4, 4, subbandParams);

    // Lossy: restored should be close but not exact
    Assert.That(restored[0], Is.EqualTo(coeffs[0]).Within(2.0));
    Assert.That(restored[1], Is.EqualTo(coeffs[1]).Within(2.0));
    Assert.That(restored[2], Is.EqualTo(0.0));
    Assert.That(restored[3], Is.EqualTo(coeffs[3]).Within(2.0));
  }
}
