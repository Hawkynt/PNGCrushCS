using System;
using System.IO;
using System.Linq;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264Intra8x8PredictionTests {

  [Test]
  public void DcWithoutNeighboursUsesMidGrey() {
    Span<byte> output = stackalloc byte[64];
    H264Intra8x8Prediction.Predict(
      2, new byte[16], new byte[8], 0,
      topAvailable: false, topRightAvailable: false, leftAvailable: false, topLeftAvailable: false,
      output);

    Assert.That(output.ToArray(), Is.All.EqualTo(128));
  }

  [Test]
  public void VerticalUsesTheFilteredTopReference() {
    byte[] top = Enumerable.Range(10, 16).Select(static value => (byte)value).ToArray();
    byte[] left = Enumerable.Repeat((byte)40, 8).ToArray();
    Span<byte> output = stackalloc byte[64];

    H264Intra8x8Prediction.Predict(
      0, top, left, topLeft: 8,
      topAvailable: true, topRightAvailable: true, leftAvailable: true, topLeftAvailable: true,
      output);

    // p'[0,-1]=(8+2*10+11+2)/4=10; p'[1,-1]=(10+2*11+12+2)/4=11.
    Assert.That(output[0], Is.EqualTo(10));
    Assert.That(output[1], Is.EqualTo(11));
    Assert.That(output.Slice(8, 8).ToArray(), Is.EqualTo(output[..8].ToArray()));
  }

  [Test]
  public void MissingTopRightRepeatsTheLastTopSampleBeforeFiltering() {
    byte[] top = Enumerable.Repeat((byte)10, 16).ToArray();
    top[7] = 200;
    for (var i = 8; i < 16; ++i)
      top[i] = 0; // must be ignored when top-right is unavailable

    Span<byte> output = stackalloc byte[64];
    H264Intra8x8Prediction.Predict(
      3, top, new byte[8], topLeft: 10,
      topAvailable: true, topRightAvailable: false, leftAvailable: false, topLeftAvailable: true,
      output);

    Assert.That(output[63], Is.EqualTo(200));
  }

  [Test]
  public void DirectionalModesRefuseMissingRequiredNeighbours() {
    Span<byte> output = stackalloc byte[64];
    Assert.That(
      () => H264Intra8x8Prediction.Predict(
        4, new byte[16], new byte[8], 0,
        topAvailable: true, topRightAvailable: false, leftAvailable: true, topLeftAvailable: false,
        output),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("top-left"));
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [TestCase(4)]
  [TestCase(5)]
  [TestCase(6)]
  [TestCase(7)]
  [TestCase(8)]
  public void EveryDefinedModeProducesACompleteBlock(int mode) {
    byte[] top = Enumerable.Range(20, 16).Select(static value => (byte)value).ToArray();
    byte[] left = Enumerable.Range(50, 8).Select(static value => (byte)value).ToArray();
    Span<byte> output = stackalloc byte[64];

    Assert.That(
      () => H264Intra8x8Prediction.Predict(
        mode, top, left, 40,
        topAvailable: true, topRightAvailable: true, leftAvailable: true, topLeftAvailable: true,
        output),
      Throws.Nothing);
  }
}
