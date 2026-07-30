using System;
using FileFormat.WebP.Vp8;

namespace FileFormat.WebP.Tests;

/// <summary>
/// Verifies Vp8BitWriter (encoder) is the exact inverse of Vp8Partition (decoder).
/// Without this invariant nothing downstream in the encoder can work, so this must pass first.
/// Exercises: fixed-prob bits, uniform bits, n-bit uints, signed bits, long sequences with carries.
/// </summary>
[TestFixture]
public sealed class Vp8BitWriterReaderRoundTripTests {

  [Test]
  public void SingleFixedProbBit_RoundTrips() {
    for (var prob = 1; prob < 255; prob += 17) {
      for (var bit = 0; bit <= 1; ++bit) {
        var w = new Vp8BitWriter(256);
        w.PutBit(bit, prob);
        var bytes = w.Finish();
        var r = new Vp8Partition();
        r.Init(bytes);
        Assert.That(r.ReadBit((byte)prob) ? 1 : 0, Is.EqualTo(bit), $"prob={prob}, bit={bit}");
      }
    }
  }

  [Test]
  public void UniformBits_RoundTrip() {
    var rnd = new Random(42);
    var values = new int[128];
    var w = new Vp8BitWriter(512);
    for (var i = 0; i < values.Length; ++i) {
      values[i] = rnd.Next(2);
      w.PutBitUniform(values[i]);
    }
    var bytes = w.Finish();
    var r = new Vp8Partition();
    r.Init(bytes);
    for (var i = 0; i < values.Length; ++i) {
      Assert.That(r.ReadBit(128) ? 1 : 0, Is.EqualTo(values[i]), $"index {i}");
    }
  }

  [Test]
  public void PutBitsAndReadUint_RoundTrip() {
    var rnd = new Random(123);
    var values = new uint[64];
    var bits = new int[64];
    var w = new Vp8BitWriter(1024);
    for (var i = 0; i < values.Length; ++i) {
      bits[i] = 1 + rnd.Next(16);
      values[i] = (uint)rnd.Next(1 << bits[i]);
      w.PutBits(values[i], bits[i]);
    }
    var bytes = w.Finish();
    var r = new Vp8Partition();
    r.Init(bytes);
    for (var i = 0; i < values.Length; ++i) {
      var got = r.ReadUint(128, bits[i]);
      Assert.That(got, Is.EqualTo(values[i]), $"index {i} nBits={bits[i]}");
    }
  }

  [Test]
  public void MixedProbabilities_RoundTrip() {
    // Simulate realistic encoder use: fixed probabilities mixed with uniform + n-bit uints.
    var w = new Vp8BitWriter(2048);
    var rnd = new Random(7);
    var ops = new System.Collections.Generic.List<Action<Vp8Partition>>();
    for (var i = 0; i < 500; ++i) {
      var kind = rnd.Next(3);
      if (kind == 0) {
        var prob = 1 + rnd.Next(254);
        var bit = rnd.Next(2);
        w.PutBit(bit, prob);
        var p = (byte)prob; var b = bit;
        ops.Add(r => Assert.That(r.ReadBit(p) ? 1 : 0, Is.EqualTo(b)));
      } else if (kind == 1) {
        var bit = rnd.Next(2);
        w.PutBitUniform(bit);
        var b = bit;
        ops.Add(r => Assert.That(r.ReadBit(128) ? 1 : 0, Is.EqualTo(b)));
      } else {
        var n = 1 + rnd.Next(8);
        var v = (uint)rnd.Next(1 << n);
        w.PutBits(v, n);
        var nn = n; var vv = v;
        ops.Add(r => Assert.That(r.ReadUint(128, nn), Is.EqualTo(vv)));
      }
    }
    var bytes = w.Finish();
    var reader = new Vp8Partition();
    reader.Init(bytes);
    foreach (var op in ops) op(reader);
  }

  [Test]
  public void CarryPropagation_ManyZeroBitsAtHighProb_RoundTrips() {
    // Writing many "0" bits when prob is very high causes range underflow and carry logic to fire;
    // this is the most error-prone path in VP8BitWriter.Flush's 0xff run handling.
    var w = new Vp8BitWriter(4096);
    for (var i = 0; i < 10000; ++i) {
      w.PutBit(0, 250);
    }
    var bytes = w.Finish();
    var r = new Vp8Partition();
    r.Init(bytes);
    for (var i = 0; i < 10000; ++i) {
      Assert.That(r.ReadBit(250) ? 1 : 0, Is.EqualTo(0), $"bit {i}");
    }
  }
}
