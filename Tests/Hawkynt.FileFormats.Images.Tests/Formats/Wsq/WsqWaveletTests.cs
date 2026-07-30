using System;
using FileFormat.Wsq;

namespace FileFormat.Wsq.Tests;

[TestFixture]
public sealed class WsqWaveletTests {

  [Test]
  [Category("Unit")]
  public void Forward1D_InverseRoundTrip_SmallSignal() {
    var input = new double[] { 100, 120, 130, 110, 90, 80, 100, 120 };
    WsqWavelet._Forward1D(input, input.Length, out var lo, out var hi);
    var output = new double[input.Length];
    WsqWavelet._Inverse1D(lo, hi, input.Length, output);

    for (var i = 0; i < input.Length; ++i)
      Assert.That(output[i], Is.EqualTo(input[i]).Within(1e-10));
  }

  [Test]
  [Category("Unit")]
  public void Forward1D_InverseRoundTrip_OddLength() {
    var input = new double[] { 50, 100, 150, 200, 250 };
    WsqWavelet._Forward1D(input, input.Length, out var lo, out var hi);
    var output = new double[input.Length];
    WsqWavelet._Inverse1D(lo, hi, input.Length, output);

    for (var i = 0; i < input.Length; ++i)
      Assert.That(output[i], Is.EqualTo(input[i]).Within(1e-10));
  }

  [Test]
  [Category("Unit")]
  public void Forward1D_ProducesCorrectSubbandSizes() {
    var input = new double[] { 1, 2, 3, 4, 5, 6, 7, 8 };
    WsqWavelet._Forward1D(input, input.Length, out var lo, out var hi);

    Assert.That(lo.Length, Is.EqualTo(4)); // (8+1)/2 = 4
    Assert.That(hi.Length, Is.EqualTo(4)); // 8/2 = 4
  }

  [Test]
  [Category("Unit")]
  public void Forward1D_OddLength_ProducesCorrectSubbandSizes() {
    var input = new double[] { 1, 2, 3, 4, 5 };
    WsqWavelet._Forward1D(input, input.Length, out var lo, out var hi);

    Assert.That(lo.Length, Is.EqualTo(3)); // (5+1)/2 = 3
    Assert.That(hi.Length, Is.EqualTo(2)); // 5/2 = 2
  }

  [Test]
  [Category("Unit")]
  public void Forward2D_Inverse2D_RoundTrip() {
    const int size = 32;
    var pixels = new byte[size * size];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(128 + 50 * Math.Sin(i * 0.1));

    var coeffs = WsqWavelet.Forward2D(pixels, size, size);
    var restored = WsqWavelet.Inverse2D(coeffs, size, size);

    for (var i = 0; i < pixels.Length; ++i)
      Assert.That((int)restored[i], Is.EqualTo((int)pixels[i]).Within(1));
  }

  [Test]
  [Category("Unit")]
  public void Forward2D_Inverse2D_UniformInput_Preserved() {
    const int size = 16;
    var pixels = new byte[size * size];
    Array.Fill(pixels, (byte)128);

    var coeffs = WsqWavelet.Forward2D(pixels, size, size);
    var restored = WsqWavelet.Inverse2D(coeffs, size, size);

    for (var i = 0; i < pixels.Length; ++i)
      Assert.That((int)restored[i], Is.EqualTo(128).Within(1));
  }

  [Test]
  [Category("Unit")]
  public void MirrorIndex_NegativeIndex_Mirrors() {
    var data = new double[] { 10, 20, 30, 40 };
    Assert.That(WsqWavelet._MirrorIndex(data, -1, 4), Is.EqualTo(20));
    Assert.That(WsqWavelet._MirrorIndex(data, -2, 4), Is.EqualTo(30));
  }

  [Test]
  [Category("Unit")]
  public void MirrorIndex_BeyondEnd_Mirrors() {
    var data = new double[] { 10, 20, 30, 40 };
    Assert.That(WsqWavelet._MirrorIndex(data, 4, 4), Is.EqualTo(30));
    Assert.That(WsqWavelet._MirrorIndex(data, 5, 4), Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void MirrorIndex_InRange_ReturnsOriginal() {
    var data = new double[] { 10, 20, 30, 40 };
    Assert.That(WsqWavelet._MirrorIndex(data, 0, 4), Is.EqualTo(10));
    Assert.That(WsqWavelet._MirrorIndex(data, 3, 4), Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void ComputeSubbandLayout_ReturnsCorrectCount() {
    var subbands = WsqWavelet.ComputeSubbandLayout(64, 64);
    Assert.That(subbands.Length, Is.EqualTo(WsqWavelet.NUM_SUBBANDS));
  }

  [Test]
  [Category("Unit")]
  public void ComputeSubbandLayout_LLSubbandAtOrigin() {
    var subbands = WsqWavelet.ComputeSubbandLayout(64, 64);
    Assert.That(subbands[0].X, Is.EqualTo(0));
    Assert.That(subbands[0].Y, Is.EqualTo(0));
    Assert.That(subbands[0].Width, Is.EqualTo(2)); // 64 >> 5 = 2
    Assert.That(subbands[0].Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void Forward1D_ConstantInput_HighPassNearZero() {
    var input = new double[] { 100, 100, 100, 100, 100, 100, 100, 100 };
    WsqWavelet._Forward1D(input, input.Length, out _, out var hi);

    for (var i = 0; i < hi.Length; ++i)
      Assert.That(hi[i], Is.EqualTo(0).Within(1e-6));
  }
}
